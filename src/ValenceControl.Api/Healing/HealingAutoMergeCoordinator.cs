using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Healing;

public sealed class HealingAutoMergeCoordinator(
    HealingDbContext dbContext,
    HealingMergeService mergeService,
    IRepairMergeProvider mergeProvider,
    ITrustedDeploymentSafetyCapabilitySource deploymentSafetyCapabilities,
    ProviderOperationService providerOperations,
    TimeProvider timeProvider,
    IOptions<HealingOptions> options)
{
    private static readonly TimeSpan MaximumProviderSnapshotAge = TimeSpan.FromMinutes(5);
    private readonly HealingOptions _options = options.Value;

    public async ValueTask<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (await RecoverAllowedUnclaimedEvaluationAsync(cancellationToken))
            return true;

        var candidate = await (
            from pullRequest in dbContext.RepairPullRequests.AsNoTracking()
            join attempt in dbContext.RepairAttempts.AsNoTracking()
                on new { pullRequest.WorkspaceId, pullRequest.ApplicationId, Id = pullRequest.AttemptId }
                equals new { attempt.WorkspaceId, attempt.ApplicationId, attempt.Id }
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
            join provider in dbContext.ProviderConnections.AsNoTracking()
                on new { pullRequest.WorkspaceId, Id = pullRequest.ProviderConnectionId }
                equals new { provider.WorkspaceId, provider.Id }
            where pullRequest.MergeState == PullRequestMergeState.Open &&
                  pullRequest.MergePolicyEvaluationId == null &&
                  pullRequest.ClosureReason == null &&
                  attempt.Status == RepairAttemptStatus.PullRequestOpen &&
                  binding.Status == SourceOwnershipBindingStatus.Active &&
                  provider.Status == ProviderConnectionStatus.Active
            orderby pullRequest.Id
            select new { PullRequest = pullRequest, Attempt = attempt, Binding = binding, Provider = provider })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
            return false;

        var policy = await dbContext.MergePolicies.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == candidate.Binding.MergePolicyId &&
                 x.WorkspaceId == candidate.Attempt.WorkspaceId &&
                 x.ApplicationId == candidate.Attempt.ApplicationId,
            cancellationToken);
        var result = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == candidate.Attempt.Id,
            cancellationToken);
        var configuration = await dbContext.HealingConfigurations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == candidate.Attempt.WorkspaceId && x.ApplicationId == candidate.Attempt.ApplicationId,
            cancellationToken);
        var workspace = await dbContext.HealingWorkspaceConfigurations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == candidate.Attempt.WorkspaceId,
            cancellationToken);
        var pathPolicy = await dbContext.PathPolicies.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == candidate.Binding.PathPolicyId &&
                 x.WorkspaceId == candidate.Attempt.WorkspaceId &&
                 x.ApplicationId == candidate.Attempt.ApplicationId,
            cancellationToken);
        if (policy is null || result is null || configuration is null || workspace is null || pathPolicy is null)
        {
            await BlockAsync(candidate.PullRequest.Id, cancellationToken);
            return true;
        }
        var publicationEvaluation = await dbContext.PolicyEvaluations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.WorkspaceId == candidate.Attempt.WorkspaceId &&
            x.ApplicationId == candidate.Attempt.ApplicationId &&
            x.AttemptId == candidate.Attempt.Id &&
            x.PolicyId == pathPolicy.Id &&
            x.PolicyKind == PolicyKind.Path &&
            x.PolicyVersion == pathPolicy.PolicyVersion &&
            x.PolicyHash == pathPolicy.PolicyHash &&
            x.InputSnapshotHash == result.EnvelopeDigest &&
            x.Decision == PolicyDecision.AllowPublication,
            cancellationToken);

        var producingRevisionVerified = candidate.Attempt.ProducingRevision is not null &&
            candidate.Attempt.RepairClassification != RepairClassification.RevisionUnverified &&
            await dbContext.ComponentManifests.AsNoTracking().AnyAsync(x =>
                x.WorkspaceId == candidate.Attempt.WorkspaceId &&
                x.ApplicationId == candidate.Attempt.ApplicationId &&
                x.SourceRevision == candidate.Attempt.ProducingRevision &&
                x.TrustState == ComponentManifestTrustState.Verified,
                cancellationToken);

        ProviderMergeSnapshot snapshot;
        try
        {
            snapshot = await mergeProvider.GetMergeSnapshotAsync(
                Repository(candidate.Provider),
                candidate.PullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken);
        }
        catch
        {
            // Provider reads are retriable. A transient outage must never become a durable policy denial.
            throw;
        }

        var deploymentSafety = await deploymentSafetyCapabilities.GetAsync(
            candidate.Attempt.WorkspaceId,
            candidate.Attempt.ApplicationId,
            candidate.Attempt.EpisodeId,
            cancellationToken);

        var inputDigest = Digest(JsonSerializer.Serialize(new
        {
            candidate.PullRequest.Id,
            candidate.PullRequest.HeadRevision,
            candidate.PullRequest.PatchDigest,
            result.EnvelopeDigest,
            policy.PolicyHash,
            DeploymentSafetyDigest = deploymentSafety.Digest,
            Snapshot = snapshot
        }));
        var observations = BuildObservations(
            candidate.Attempt,
            candidate.PullRequest,
            result,
            pathPolicy,
            policy,
            configuration,
            workspace,
            snapshot,
            deploymentSafety,
            producingRevisionVerified,
            publicationEvaluation is not null);
        var evaluation = await mergeService.EvaluateAsync(new(
            candidate.Attempt.WorkspaceId,
            candidate.Attempt.ApplicationId,
            candidate.Attempt.Id,
            candidate.PullRequest.Id,
            policy,
            new(inputDigest, observations),
            candidate.Attempt.IncidentId,
            candidate.Attempt.EpisodeId), cancellationToken);
        if (!evaluation.AutomaticMergeAllowed)
            return true;

        var idempotencyKey = $"merge:{candidate.PullRequest.Id:N}:{candidate.PullRequest.HeadRevision}:{evaluation.Evaluation.Id:N}";
        var mergeRequest = new ProviderMergeRequest(
            HealingContractVersions.ProviderProtocol,
            Repository(candidate.Provider),
            candidate.PullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            candidate.PullRequest.HeadRevision,
            evaluation.Snapshot,
            idempotencyKey);
        await ClaimAndEnqueueAsync(
            candidate.PullRequest.Id,
            evaluation.Evaluation.Id,
            new(
                candidate.Attempt.WorkspaceId,
                candidate.Attempt.ApplicationId,
                candidate.Provider.Id,
                ProviderOperationKind.RequestMerge,
                idempotencyKey,
                JsonSerializer.Serialize(mergeRequest),
                candidate.Attempt.IncidentId,
                candidate.Attempt.Id),
            cancellationToken);
        return true;
    }

    private IReadOnlyList<RepairPolicyObservation> BuildObservations(
        RepairAttempt attempt,
        RepairPullRequest pullRequest,
        RepairResult result,
        PathPolicy pathPolicy,
        MergePolicy mergePolicy,
        HealingConfiguration configuration,
        HealingWorkspaceConfiguration workspace,
        ProviderMergeSnapshot snapshot,
        TrustedDeploymentSafetyCapabilitySnapshot deploymentSafety,
        bool producingRevisionVerified,
        bool publicationEvaluationBound)
    {
        var reproduction = Deserialize<RepairReproductionEvidence>(result.ReproductionJson);
        var regression = Deserialize<RepairRegressionEvidence>(result.RegressionJson);
        var changedPaths = Deserialize<RepairChangedPathSuggestion[]>(result.ChangedPathsJson) ?? [];
        var forbiddenCategories = Strings(mergePolicy.ForbiddenChangeCategoriesJson);
        var configuredChecks = Strings(mergePolicy.RequiredChecksJson);
        var allowedRoots = Strings(pathPolicy.AllowedRootsJson);
        var checksPass = configuredChecks.All(required => snapshot.RequiredChecks.Contains(required, StringComparer.Ordinal) && snapshot.Checks.Any(x =>
            x.Name == required && x.State.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            x.Revision == pullRequest.HeadRevision));
        var independentPass = !string.IsNullOrWhiteSpace(mergePolicy.IndependentVerifier) &&
                              snapshot.RequiredChecks.Contains(mergePolicy.IndependentVerifier, StringComparer.Ordinal) &&
                              snapshot.Checks.Any(x => x.Name == mergePolicy.IndependentVerifier &&
                                  x.State.Equals("success", StringComparison.OrdinalIgnoreCase) && x.Revision == pullRequest.HeadRevision);
        ParsedUnifiedDiff? parsedPatch;
        try { parsedPatch = UnifiedDiffParser.Parse(result.UnifiedDiff, pathPolicy.MaxPatchBytes); }
        catch (GitHubSecurityException) { parsedPatch = null; }
        var actualPaths = parsedPatch?.Files.Select(x => x.EffectivePath).Order(StringComparer.Ordinal).ToArray() ?? [];
        var reportedPaths = changedPaths.Select(x => x.Path).Order(StringComparer.Ordinal).ToArray();
        var pathMetadataBound = actualPaths.Length > 0 && actualPaths.SequenceEqual(reportedPaths, StringComparer.Ordinal) &&
                                reportedPaths.Distinct(StringComparer.Ordinal).Count() == reportedPaths.Length;
        var changeSizeAllowed = parsedPatch is not null && parsedPatch.Files.Count <= pathPolicy.MaxFiles &&
                                parsedPatch.ChangedLines <= pathPolicy.MaxChangedLines && parsedPatch.SizeBytes <= pathPolicy.MaxPatchBytes;
        var observedCategories = parsedPatch is null
            ? new HashSet<string>(["unknown"], StringComparer.Ordinal)
            : RepairChangeRiskClassifier.Classify(parsedPatch.Files.Select(file => new RepairDiffFileObservation(
                file.EffectivePath,
                file.Hunks.SelectMany(hunk => hunk.Lines)
                    .Where(line => line.Kind is '+' or '-')
                    .Select(line => line.Text)
                    .ToArray())).ToArray());
        var lowRisk = pathMetadataBound && observedCategories.Count == 0 &&
                      actualPaths.All(x => IsUnderAllowedRoot(x, allowedRoots));
        var categoriesClear = pathMetadataBound && observedCategories.Count == 0 && changedPaths.All(x =>
            x.RiskCategory is not null && !forbiddenCategories.Contains(x.RiskCategory));
        var now = timeProvider.GetUtcNow();

        return
        [
            Observation(AutoMergePolicyGates.Publication,
                pullRequest.PatchDigest == result.PatchDigest && attempt.Status == RepairAttemptStatus.PullRequestOpen &&
                snapshot.IsOpen && !snapshot.IsDraft && publicationEvaluationBound,
                "trusted-publication-bound"),
            Observation(AutoMergePolicyGates.ProducingRevision, producingRevisionVerified, "producing-revision-verified"),
            Observation(AutoMergePolicyGates.Reproduction,
                result.Classification == RepairClassification.Reproduced && reproduction?.WasReproduced == true,
                "failure-reproduced"),
            Observation(AutoMergePolicyGates.RegressionBefore, regression?.FailedBeforePatch == true, "regression-failed-before-patch"),
            Observation(AutoMergePolicyGates.RegressionAfter, regression?.PassedAfterPatch == true, "regression-passed-after-patch"),
            Observation(AutoMergePolicyGates.IndependentVerification, independentPass, "independent-verification-passed"),
            Observation(AutoMergePolicyGates.RequiredChecks, checksPass, "required-checks-passed"),
            Observation(AutoMergePolicyGates.BranchProtection, snapshot.IsBranchProtectionSatisfied, "branch-protection-satisfied"),
            Observation(AutoMergePolicyGates.LowRiskPaths, lowRisk, "low-risk-paths"),
            Observation(AutoMergePolicyGates.ChangeSize, changeSizeAllowed, "change-size-allowed"),
            Observation(AutoMergePolicyGates.ChangeCategories, categoriesClear, "change-categories-allowed"),
            new RepairPolicyObservation(
                AutoMergePolicyGates.RollbackOrStop,
                deploymentSafety.State,
                deploymentSafety.ReasonCode),
            Observation(AutoMergePolicyGates.HeadRevision,
                snapshot.HeadRevision == pullRequest.HeadRevision && snapshot.BaseRevision == pullRequest.BaseRevision,
                "head-revision-bound"),
            Observation(AutoMergePolicyGates.ProviderSnapshot,
                snapshot.ObservedAt <= now && now - snapshot.ObservedAt <= MaximumProviderSnapshotAge,
                "provider-snapshot-fresh"),
            Observation(AutoMergePolicyGates.KillSwitches,
                _options.AutomaticMergeEnabled && !_options.ControlKillSwitch &&
                configuration.AutomaticMergeEnabled && !configuration.ApplicationKillSwitch && !workspace.WorkspaceKillSwitch,
                "kill-switches-clear")
        ];
    }

    private static RepairPolicyObservation Observation(string gate, bool passed, string successReason) =>
        new(gate, passed ? RepairPolicyObservationState.Satisfied : RepairPolicyObservationState.Failed,
            passed ? successReason : $"{gate}-blocked");

    private static T? Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return default; }
    }

    private static IReadOnlySet<string> Strings(string json) =>
        (Deserialize<string[]>(json) ?? []).ToHashSet(StringComparer.Ordinal);

    private static bool IsUnderAllowedRoot(string path, IReadOnlySet<string> allowedRoots)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.Length > 0 && !normalized.Split('/').Contains("..", StringComparer.Ordinal) &&
               allowedRoots.Any(root =>
               {
                   var normalizedRoot = root.Replace('\\', '/').Trim('/');
                   return normalizedRoot.Length > 0 &&
                          (normalized.Equals(normalizedRoot, StringComparison.Ordinal) ||
                           normalized.StartsWith($"{normalizedRoot}/", StringComparison.Ordinal));
               });
    }

    private static ProviderRepositoryReference Repository(ProviderConnection provider) =>
        new(provider.Id, provider.RepositoryProviderId, provider.RepositoryOwner, provider.RepositoryName);

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async ValueTask<bool> ClaimAndEnqueueAsync(
        Guid pullRequestId,
        Guid evaluationId,
        ProviderOperationEnqueueRequest operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await dbContext.RepairPullRequests
                .Where(x => x.Id == pullRequestId &&
                            x.MergeState == PullRequestMergeState.Open &&
                            x.MergePolicyEvaluationId == evaluationId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.MergeState, PullRequestMergeState.MergeRequested)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
            if (claimed != 1)
                return false;

            await providerOperations.EnqueueAsync(operation, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    private async ValueTask<bool> RecoverAllowedUnclaimedEvaluationAsync(CancellationToken cancellationToken)
    {
        var staleBefore = timeProvider.GetUtcNow() - _options.LeaseDuration;
        var recovered = await dbContext.RepairPullRequests
            .Where(pullRequest =>
                pullRequest.MergeState == PullRequestMergeState.Open &&
                pullRequest.MergePolicyEvaluationId != null &&
                pullRequest.ClosureReason == null &&
                dbContext.RepairAttempts.Any(attempt =>
                    attempt.Id == pullRequest.AttemptId &&
                    attempt.WorkspaceId == pullRequest.WorkspaceId &&
                    attempt.ApplicationId == pullRequest.ApplicationId &&
                    attempt.Status == RepairAttemptStatus.PullRequestOpen) &&
                dbContext.PolicyEvaluations.Any(evaluation =>
                    evaluation.Id == pullRequest.MergePolicyEvaluationId &&
                    evaluation.WorkspaceId == pullRequest.WorkspaceId &&
                    evaluation.ApplicationId == pullRequest.ApplicationId &&
                    evaluation.AttemptId == pullRequest.AttemptId &&
                    evaluation.Decision == PolicyDecision.AllowAutomaticMerge &&
                    evaluation.EvaluatedAt <= staleBefore))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.MergePolicyEvaluationId, (Guid?)null)
                .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
        return recovered > 0;
    }

    private async ValueTask BlockAsync(Guid pullRequestId, CancellationToken cancellationToken) =>
        await dbContext.RepairPullRequests.Where(x => x.Id == pullRequestId && x.MergePolicyEvaluationId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ClosureReason, "merge-authority-unavailable")
                .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
}
