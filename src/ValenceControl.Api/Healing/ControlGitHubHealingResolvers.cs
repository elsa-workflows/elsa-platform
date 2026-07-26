using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Healing;

public sealed class ControlGitHubRepositoryAuthorizationResolver(
    HealingDbContext dbContext,
    IHealingProviderCredentialResolver credentialResolver) : IGitHubRepositoryAuthorizationResolver
{
    public async ValueTask<GitHubRepositoryAuthorization?> ResolveAsync(
        Guid providerConnectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.ProviderConnections.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == providerConnectionId &&
                 x.Status == ProviderConnectionStatus.Active &&
                 x.Provider == "GitHub",
            cancellationToken);
        if (connection is null)
            return null;

        var protectedCredential = await credentialResolver.ResolveAsync(
            connection.WorkspaceId,
            connection.CredentialReference,
            cancellationToken);
        if (!GitHubAppCredential.TryParse(protectedCredential, out var credential) || credential is null)
            return null;

        var workflows = await dbContext.SourceOwnershipBindings.AsNoTracking()
            .Where(x => x.WorkspaceId == connection.WorkspaceId &&
                        x.ProviderConnectionId == connection.Id &&
                        x.Status == SourceOwnershipBindingStatus.Active)
            .Select(x => new { x.WorkflowIdentity, x.WorkflowReference, x.WorkflowRevision })
            .ToArrayAsync(cancellationToken);
        var conflictingWorkflow = workflows.GroupBy(x => x.WorkflowIdentity, StringComparer.Ordinal)
            .Any(group => group.Select(x => $"{x.WorkflowReference}\n{x.WorkflowRevision}").Distinct(StringComparer.Ordinal).Count() > 1);
        if (conflictingWorkflow)
            return null;

        return new GitHubRepositoryAuthorization(
            connection.Id,
            connection.RepositoryProviderId,
            connection.RepositoryOwner,
            connection.RepositoryName,
            connection.InstallationId,
            credential,
            workflows.GroupBy(x => x.WorkflowIdentity, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => new GitHubApprovedWorkflow(
                        x.First().WorkflowIdentity,
                        x.First().WorkflowReference,
                        x.First().WorkflowRevision),
                    StringComparer.Ordinal));
    }
}

public sealed class ControlTrustedGitHubPublicationContextResolver(
    HealingDbContext dbContext,
    IGitHubRepositoryAuthorizationResolver authorizationResolver) : ITrustedGitHubPublicationContextResolver
{
    public async ValueTask<TrustedGitHubPublicationContext?> ResolveAsync(
        RepairPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var attempt = await dbContext.RepairAttempts.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.AttemptId &&
                 x.IncidentId == request.IncidentId &&
                 x.EpisodeId == request.EpisodeId &&
                 x.TargetRevision == request.ExpectedTargetRevision,
            cancellationToken);
        if (attempt is null)
            return null;
        var binding = await dbContext.SourceOwnershipBindings.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.Id == attempt.BindingId &&
                 x.ProviderConnectionId == request.Repository.ProviderConnectionId &&
                 x.Status == SourceOwnershipBindingStatus.Active,
            cancellationToken);
        if (binding is null)
            return null;
        if (!string.Equals(binding.TargetBranch, request.TargetBranch, StringComparison.Ordinal))
            return null;
        var pathPolicy = await dbContext.PathPolicies.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == binding.WorkspaceId &&
                 x.ApplicationId == binding.ApplicationId &&
                 x.Id == binding.PathPolicyId,
            cancellationToken);
        if (pathPolicy is null)
            return null;
        var persistedResult = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.AttemptId == attempt.Id,
            cancellationToken);
        if (persistedResult is null ||
            persistedResult.PatchDigest != request.Result.PatchDigest ||
            persistedResult.EnvelopeDigest != ComputeEnvelopeDigest(request.Result) ||
            persistedResult.TargetRevision != request.ExpectedTargetRevision)
            return null;
        var expectedDecision = request.PublicationPolicy.Decision == PolicyDecisions.AllowPublication
            ? PolicyDecision.AllowPublication
            : PolicyDecision.Deny;
        var persistedEvaluation = await dbContext.PolicyEvaluations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.AttemptId == attempt.Id &&
                 x.PolicyId == pathPolicy.Id &&
                 x.PolicyKind == PolicyKind.Path &&
                 x.PolicyVersion == request.PublicationPolicy.PolicyVersion &&
                 x.PolicyHash == request.PublicationPolicy.PolicyHash &&
                 x.InputSnapshotHash == request.PublicationPolicy.InputDigest &&
                 x.Decision == expectedDecision &&
                 x.EvaluatedAt == request.PublicationPolicy.EvaluatedAt,
            cancellationToken);
        if (persistedEvaluation is null ||
            CanonicalJson(persistedEvaluation.GateResultsJson) != CanonicalJson(JsonSerializer.Serialize(request.PublicationPolicy.Gates)))
            return null;
        var authorization = await authorizationResolver.ResolveAsync(binding.ProviderConnectionId, cancellationToken);
        if (authorization is null)
            return null;

        return new TrustedGitHubPublicationContext(
            authorization,
            new TrustedGitHubPublicationPolicy(
                pathPolicy.PolicyVersion,
                pathPolicy.PolicyHash,
                ParseStringArray(pathPolicy.AllowedRootsJson),
                ParseStringArray(pathPolicy.ForbiddenRootsJson),
                pathPolicy.MaxFiles,
                pathPolicy.MaxChangedLines,
                pathPolicy.MaxPatchBytes),
            (await dbContext.EvidenceBundles.AsNoTracking().SingleAsync(
                x => x.WorkspaceId == attempt.WorkspaceId &&
                     x.ApplicationId == attempt.ApplicationId &&
                     x.Id == attempt.EvidenceBundleId,
                cancellationToken)).Tier == EvidenceTier.Elevated ? "elevated" : "default-redacted",
            string.IsNullOrWhiteSpace(attempt.ProducingRevision)
                ? "unavailable"
                : attempt.RepairClassification == RepairClassification.RevisionUnverified ? "unverified" : "verified");
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ComputeEnvelopeDigest(RepairResultEnvelope result) =>
        ValenceControl.Healing.Agent.RepairAgentGateway.ComputeSha256Digest(JsonSerializer.Serialize(result));

    private static string CanonicalJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}

public sealed class ControlGitHubRepairTargetInspector(
    HealingDbContext dbContext,
    IGitHubRepositoryAuthorizationResolver authorizationResolver,
    GitHubAppTokenProvider tokenProvider,
    ITrustedGitHubRepositoryPublisher repositoryPublisher) : IRepairTargetInspector
{
    public async ValueTask<RepairTargetInspection> InspectAsync(
        RepairTargetInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = await dbContext.SourceOwnershipBindings.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == request.WorkspaceId &&
                 x.ApplicationId == request.ApplicationId &&
                 x.Id == request.BindingId &&
                 x.Status == SourceOwnershipBindingStatus.Active,
            cancellationToken);
        if (binding is null)
            return Unknown();
        var authorization = await authorizationResolver.ResolveAsync(binding.ProviderConnectionId, cancellationToken);
        if (authorization is null)
            return Unknown();

        try
        {
            var token = await tokenProvider.CreateRepositoryTokenAsync(
                authorization.Credential,
                authorization.InstallationId,
                GitHubInstallationTokenRequest.MetadataRead(authorization.Name),
                cancellationToken);
            if (token is null)
                return Unknown();
            var revision = await repositoryPublisher.GetTargetRevisionAsync(
                authorization,
                binding.TargetBranch,
                token.Value,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(revision))
                return Unknown();
            var mergedRevisions = await (
                from attempt in dbContext.RepairAttempts.AsNoTracking()
                join pullRequest in dbContext.RepairPullRequests.AsNoTracking()
                    on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.Id }
                    equals new { pullRequest.WorkspaceId, pullRequest.ApplicationId, Id = pullRequest.AttemptId }
                where attempt.WorkspaceId == request.WorkspaceId &&
                      attempt.ApplicationId == request.ApplicationId &&
                      attempt.IncidentId == request.IncidentId &&
                      attempt.BindingId == request.BindingId &&
                      pullRequest.MergeState == PullRequestMergeState.Merged &&
                      pullRequest.MergedRevision != null
                select pullRequest.MergedRevision!).Distinct().ToArrayAsync(cancellationToken);
            var alreadyFixed = false;
            foreach (var mergedRevision in mergedRevisions)
            {
                if (await repositoryPublisher.IsCommitReachableAsync(
                        authorization,
                        mergedRevision,
                        revision,
                        token.Value,
                        cancellationToken))
                {
                    alreadyFixed = true;
                    break;
                }
            }
            return new RepairTargetInspection(
                alreadyFixed ? RepairTargetState.AlreadyFixed : RepairTargetState.Unresolved,
                revision);
        }
        catch (Exception exception) when (exception is HttpRequestException or GitHubSecurityException)
        {
            return Unknown();
        }
    }

    private static RepairTargetInspection Unknown() => new(RepairTargetState.Unknown, string.Empty);
}
