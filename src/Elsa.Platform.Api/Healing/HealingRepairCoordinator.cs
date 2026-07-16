using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Agent;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Providers;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Healing;

public enum HealingRepairCoordinatorStatus { Idle, WorkItemQueued, RepairQueued, PublicationQueued }

/// <summary>
/// Advances one canonical incident state at a time. Every external mutation is first persisted in the leased
/// provider outbox; the one-use workflow nonce is encrypted before it enters that outbox.
/// </summary>
public sealed class HealingRepairCoordinator(
    HealingDbContext dbContext,
    HealingEvidenceService evidenceService,
    RepairOrchestrationService orchestrationService,
    IRepairTargetInspector targetInspector,
    ProviderOperationService providerOperations,
    IDataProtectionProvider dataProtectionProvider,
    HealingRepairAuthorityService authorityService,
    IOptions<HealingOptions> healingOptions,
    HealingGitHubOptions githubOptions,
    TimeProvider timeProvider)
{
    private readonly IDataProtector _nonceProtector = dataProtectionProvider.CreateProtector("Elsa.Platform.Healing.DispatchNonce.v1");
    private readonly HealingOptions _healingOptions = healingOptions.Value;

    public async ValueTask<HealingRepairCoordinatorStatus> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_healingOptions.PlatformKillSwitch || !_healingOptions.RepairDispatchEnabled)
            return HealingRepairCoordinatorStatus.Idle;

        await ReconcileFailedOperationsAsync(cancellationToken);
        await RecoverAbandonedAttemptsAsync(cancellationToken);

        var pendingProjections = await dbContext.RepairWorkItemProjections.AsNoTracking()
            .Where(x => x.ProjectionStatus == WorkItemProjectionStatus.Pending)
            .OrderBy(x => x.Id)
            .Take(32)
            .ToArrayAsync(cancellationToken);
        foreach (var pendingProjection in pendingProjections)
        {
            if (await authorityService.CanMutateAsync(
                    pendingProjection.WorkspaceId, pendingProjection.ApplicationId, pendingProjection.EpisodeId,
                    pendingProjection.ProviderConnectionId, pendingProjection.IncidentId, null, cancellationToken))
                return await QueueWorkItemAsync(pendingProjection, cancellationToken);
        }

        var currentProjections = await dbContext.RepairWorkItemProjections.AsNoTracking()
            .Where(x => x.ProjectionStatus == WorkItemProjectionStatus.Current)
            .Where(x => dbContext.HealingIncidents.Any(incident =>
                incident.Id == x.IncidentId && incident.Status != HealingIncidentStatus.NeedsHuman))
            .Where(x => !dbContext.RepairAttempts.Any(a =>
                a.WorkspaceId == x.WorkspaceId && a.ApplicationId == x.ApplicationId && a.EpisodeId == x.EpisodeId &&
                (a.Status == RepairAttemptStatus.Queued || a.Status == RepairAttemptStatus.Dispatched ||
                 a.Status == RepairAttemptStatus.Running || a.Status == RepairAttemptStatus.ProposalReady ||
                 a.Status == RepairAttemptStatus.ResultReceived ||
                 a.Status == RepairAttemptStatus.Publishing || a.Status == RepairAttemptStatus.PullRequestOpen)))
            .OrderBy(x => x.LastProjectedAt)
            .ThenBy(x => x.Id)
            .Take(32)
            .ToArrayAsync(cancellationToken);
        foreach (var currentProjection in currentProjections)
        {
            var attemptCount = await dbContext.RepairAttempts.AsNoTracking().CountAsync(
                x => x.WorkspaceId == currentProjection.WorkspaceId &&
                     x.ApplicationId == currentProjection.ApplicationId &&
                     x.EpisodeId == currentProjection.EpisodeId,
                cancellationToken);
            var applicationLimits = await dbContext.HealingConfigurations.AsNoTracking().SingleOrDefaultAsync(
                x => x.WorkspaceId == currentProjection.WorkspaceId && x.ApplicationId == currentProjection.ApplicationId,
                cancellationToken);
            var effectiveAttemptLimit = applicationLimits is null
                ? 0
                : Math.Min(_healingOptions.Budgets.MaxRepairAttempts, applicationLimits.DefaultAttemptLimit);
            if (effectiveAttemptLimit < 1 || attemptCount >= effectiveAttemptLimit)
            {
                await MarkNeedsHumanAsync(currentProjection.IncidentId, NeedsHumanReason.AttemptLimitReached, cancellationToken);
                continue;
            }
            if (await authorityService.CanMutateAsync(
                    currentProjection.WorkspaceId, currentProjection.ApplicationId, currentProjection.EpisodeId,
                    currentProjection.ProviderConnectionId, currentProjection.IncidentId, null, cancellationToken))
                return await QueueRepairAsync(currentProjection, cancellationToken);
        }

        var completedAttempts = await dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.Status == RepairAttemptStatus.ResultReceived)
            .Where(x => !dbContext.RepairPullRequests.Any(p => p.AttemptId == x.Id))
            .OrderBy(x => x.CompletedAt)
            .ThenBy(x => x.Id)
            .Take(32)
            .ToArrayAsync(cancellationToken);
        foreach (var completedAttempt in completedAttempts)
        {
            var completedBinding = await dbContext.SourceOwnershipBindings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == completedAttempt.BindingId, cancellationToken);
            if (completedBinding is null || !await authorityService.CanMutateAsync(
                    completedAttempt.WorkspaceId, completedAttempt.ApplicationId, completedAttempt.EpisodeId,
                    completedBinding.ProviderConnectionId, completedAttempt.IncidentId, completedAttempt.Id, cancellationToken))
            {
                await StopAttemptAsync(completedAttempt, NeedsHumanReason.PolicyBlocked, "healing-authority-revoked", cancellationToken);
                continue;
            }
            var queued = await QueuePublicationAsync(completedAttempt, cancellationToken);
            if (queued != HealingRepairCoordinatorStatus.Idle)
                return queued;
        }
        return HealingRepairCoordinatorStatus.Idle;
    }

    private async ValueTask<HealingRepairCoordinatorStatus> QueueWorkItemAsync(
        RepairWorkItemProjection projection,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.HealingIncidents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == projection.WorkspaceId &&
                 x.ApplicationId == projection.ApplicationId &&
                 x.Id == projection.IncidentId &&
                 x.ActiveEpisodeId == projection.EpisodeId,
            cancellationToken);
        var provider = await dbContext.ProviderConnections.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == projection.WorkspaceId &&
                 x.Id == projection.ProviderConnectionId &&
                 x.Status == ProviderConnectionStatus.Active,
            cancellationToken);
        if (incident is null || provider is null)
            return HealingRepairCoordinatorStatus.Idle;

        var summary = JsonSerializer.Serialize(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["classification"] = incident.Classification.ToString(),
            ["firstSeenAt"] = incident.FirstSeenAt,
            ["lastSeenAt"] = incident.LastSeenAt,
            ["occurrenceCount"] = incident.OccurrenceCount,
            ["severity"] = incident.Severity.ToString(),
            ["state"] = incident.Status.ToString()
        });
        var request = new RepairWorkItemUpsertRequest(
            HealingContractVersions.ProviderProtocol,
            Repository(provider),
            incident.Id,
            projection.EpisodeId,
            $"Elsa Healing incident {incident.Fingerprint[..Math.Min(12, incident.Fingerprint.Length)]}",
            summary,
            Sha256(summary),
            $"work-item:{projection.Id:N}:{Sha256(summary)}");
        var enqueued = await providerOperations.EnqueueAsync(new ProviderOperationEnqueueRequest(
            projection.WorkspaceId,
            projection.ApplicationId,
            projection.ProviderConnectionId,
            ProviderOperationKind.UpsertWorkItem,
            request.IdempotencyKey,
            JsonSerializer.Serialize(request),
            projection.IncidentId), cancellationToken);
        return enqueued.IsReplay ? HealingRepairCoordinatorStatus.Idle : HealingRepairCoordinatorStatus.WorkItemQueued;
    }

    private async ValueTask<HealingRepairCoordinatorStatus> QueueRepairAsync(
        RepairWorkItemProjection projection,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(githubOptions.PlatformBaseUrl, UriKind.Absolute, out var platformBaseUrl) ||
            platformBaseUrl.Scheme != Uri.UriSchemeHttps)
            return HealingRepairCoordinatorStatus.Idle;
        var incident = await dbContext.HealingIncidents.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == projection.WorkspaceId &&
                 x.ApplicationId == projection.ApplicationId &&
                 x.Id == projection.IncidentId &&
                 x.ActiveEpisodeId == projection.EpisodeId &&
                 x.SelectedBindingId != null,
            cancellationToken);
        if (incident?.SelectedBindingId is not { } bindingId)
            return HealingRepairCoordinatorStatus.Idle;
        var binding = await dbContext.SourceOwnershipBindings.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == projection.WorkspaceId &&
                 x.ApplicationId == projection.ApplicationId &&
                 x.Id == bindingId &&
                 x.Status == SourceOwnershipBindingStatus.Active,
            cancellationToken);
        var provider = await dbContext.ProviderConnections.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == projection.WorkspaceId &&
                 x.Id == projection.ProviderConnectionId &&
                 x.Status == ProviderConnectionStatus.Active,
            cancellationToken);
        if (binding is null || provider is null)
            return HealingRepairCoordinatorStatus.Idle;

        var target = await targetInspector.InspectAsync(new RepairTargetInspectionRequest(
            projection.WorkspaceId, projection.ApplicationId, projection.IncidentId, projection.EpisodeId,
            binding.Id, string.Empty), cancellationToken);
        if (target.State != RepairTargetState.Unresolved || string.IsNullOrWhiteSpace(target.CurrentTargetRevision))
            return HealingRepairCoordinatorStatus.Idle;
        var evidence = await evidenceService.CreateDefaultAsync(
            EvidenceBundleRequest.CreateDefault(projection.WorkspaceId, projection.ApplicationId, projection.IncidentId),
            cancellationToken);
        if (!evidence.Succeeded || evidence.Bundle is null)
            return HealingRepairCoordinatorStatus.Idle;

        var episode = await dbContext.IncidentEpisodes.AsNoTracking().SingleAsync(
            x => x.Id == projection.EpisodeId, cancellationToken);
        var producingRevisions = ParseRevisions(episode.ProducingRevisionsJson)
            .Where(IsGitRevision)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var producingRevision = producingRevisions.Length == 1 ? producingRevisions[0] : null;
        var producingRevisionVerified = producingRevision is not null && await dbContext.ComponentManifests.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == projection.WorkspaceId &&
                 x.ApplicationId == projection.ApplicationId &&
                 x.SourceRevision == producingRevision &&
                 x.TrustState == ComponentManifestTrustState.Verified,
            cancellationToken);
        var applicationConfiguration = await dbContext.HealingConfigurations.AsNoTracking().SingleAsync(
            x => x.WorkspaceId == projection.WorkspaceId && x.ApplicationId == projection.ApplicationId,
            cancellationToken);
        var activeApplicationAttempts = await dbContext.RepairAttempts.AsNoTracking().CountAsync(x =>
            x.WorkspaceId == projection.WorkspaceId &&
            x.ApplicationId == projection.ApplicationId &&
            (x.Status == RepairAttemptStatus.Queued || x.Status == RepairAttemptStatus.Dispatched ||
             x.Status == RepairAttemptStatus.Running || x.Status == RepairAttemptStatus.ProposalReady ||
             x.Status == RepairAttemptStatus.ResultReceived ||
             x.Status == RepairAttemptStatus.Publishing), cancellationToken);
        var concurrencyLimit = Math.Min(_healingOptions.Budgets.MaxConcurrentOperations, applicationConfiguration.ConcurrencyBudget);
        if (concurrencyLimit < 1 || activeApplicationAttempts >= concurrencyLimit)
            return HealingRepairCoordinatorStatus.Idle;
        var maximumAttempts = Math.Min(_healingOptions.Budgets.MaxRepairAttempts, applicationConfiguration.DefaultAttemptLimit);
        if (maximumAttempts < 1)
        {
            await MarkNeedsHumanAsync(projection.IncidentId, NeedsHumanReason.PolicyBlocked, cancellationToken);
            return HealingRepairCoordinatorStatus.Idle;
        }
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var created = await orchestrationService.CreateAttemptAsync(new CreateRepairAttemptRequest(
            projection.WorkspaceId,
            projection.ApplicationId,
            projection.IncidentId,
            projection.EpisodeId,
            binding.Id,
            evidence.Bundle.Id,
            target.CurrentTargetRevision,
            producingRevision,
            producingRevisionVerified,
            JsonSerializer.Serialize(new
            {
                maxDurationSeconds = (long)Math.Min(
                    _healingOptions.Budgets.TimeBudget.TotalSeconds,
                    applicationConfiguration.TimeBudget.TotalSeconds),
                maxPatchBytes = RepairAgentGatewayLimits.MaximumPatchBytes,
                maxSteps = Math.Min(_healingOptions.Budgets.MaxRepositoryRuns, applicationConfiguration.RepositoryRunBudget),
                maxTokens = Math.Min(_healingOptions.Budgets.MaxInferenceUnits, applicationConfiguration.InferenceBudget)
            }),
            maximumAttempts), cancellationToken);
        if (!created.Succeeded || created.Attempt is null || created.OneTimeNonce is null)
            return HealingRepairCoordinatorStatus.Idle;

        var dispatch = new RepairWorkflowDispatchRequest(
            HealingContractVersions.ProviderProtocol,
            Repository(provider),
            projection.WorkspaceId,
            binding.WorkflowIdentity,
            binding.WorkflowReference,
            binding.WorkflowRevision,
            platformBaseUrl,
            projection.IncidentId,
            projection.EpisodeId,
            created.Attempt.Id,
            "dp:" + _nonceProtector.Protect(created.OneTimeNonce),
            producingRevisionVerified ? "verified" : producingRevision is null ? "unavailable" : "unverified",
            binding.TargetBranch,
            created.Attempt.TargetRevision,
            $"dispatch:{created.Attempt.Id:N}",
            githubOptions.WorkloadAudience,
            producingRevisionVerified ? producingRevision : null);
        await providerOperations.EnqueueAsync(new ProviderOperationEnqueueRequest(
            projection.WorkspaceId,
            projection.ApplicationId,
            projection.ProviderConnectionId,
            ProviderOperationKind.DispatchWorkflow,
            dispatch.IdempotencyKey,
            JsonSerializer.Serialize(dispatch),
            projection.IncidentId,
            created.Attempt.Id), cancellationToken);
        await dbContext.HealingIncidents
            .Where(x => x.Id == projection.IncidentId && x.Status == HealingIncidentStatus.ReadyForRepair)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HealingIncidentStatus.Repairing)
                .SetProperty(x => x.NeedsHumanReason, (NeedsHumanReason?)null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return HealingRepairCoordinatorStatus.RepairQueued;
    }

    private async ValueTask<HealingRepairCoordinatorStatus> QueuePublicationAsync(
        RepairAttempt attempt,
        CancellationToken cancellationToken)
    {
        var result = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == attempt.Id, cancellationToken);
        var binding = await dbContext.SourceOwnershipBindings.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId && x.ApplicationId == attempt.ApplicationId && x.Id == attempt.BindingId,
            cancellationToken);
        var provider = binding is null ? null : await dbContext.ProviderConnections.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId && x.Id == binding.ProviderConnectionId && x.Status == ProviderConnectionStatus.Active,
            cancellationToken);
        var pathPolicy = binding is null ? null : await dbContext.PathPolicies.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId && x.ApplicationId == attempt.ApplicationId && x.Id == binding.PathPolicyId,
            cancellationToken);
        var evidencePolicy = binding is null ? null : await dbContext.EvidencePolicies.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId && x.ApplicationId == attempt.ApplicationId && x.Id == binding.EvidencePolicyId,
            cancellationToken);
        var evidenceBundle = await dbContext.EvidenceBundles.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId && x.ApplicationId == attempt.ApplicationId && x.Id == attempt.EvidenceBundleId,
            cancellationToken);
        if (result is null || binding is null || provider is null || pathPolicy is null || evidencePolicy is null || evidenceBundle is null)
        {
            await StopAttemptAsync(attempt, NeedsHumanReason.PolicyBlocked, "publication-authority-unavailable", cancellationToken);
            return HealingRepairCoordinatorStatus.Idle;
        }
        if (result.Classification == RepairClassification.InsufficientConfidence)
        {
            await StopAttemptAsync(attempt, NeedsHumanReason.InsufficientConfidence, "insufficient-confidence", cancellationToken);
            return HealingRepairCoordinatorStatus.Idle;
        }

        var envelope = Rehydrate(result, attempt);
        var evidenceGates = EvaluateEvidencePolicy(evidencePolicy, evidenceBundle, result);
        var evidenceAllowed = evidenceGates.All(x => x.State == PolicyGateState.Pass);
        var now = timeProvider.GetUtcNow();
        var evidenceEvaluation = await dbContext.PolicyEvaluations.SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.AttemptId == attempt.Id &&
                 x.PolicyId == evidencePolicy.Id &&
                 x.InputSnapshotHash == result.EnvelopeDigest,
            cancellationToken);
        if (evidenceEvaluation is null)
        {
            evidenceEvaluation = new PolicyEvaluation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = attempt.WorkspaceId,
                ApplicationId = attempt.ApplicationId,
                AttemptId = attempt.Id,
                PolicyId = evidencePolicy.Id,
                PolicyKind = PolicyKind.Evidence,
                PolicyVersion = evidencePolicy.PolicyVersion,
                PolicyHash = evidencePolicy.PolicyHash,
                InputSnapshotHash = result.EnvelopeDigest,
                GateResultsJson = JsonSerializer.Serialize(evidenceGates),
                Decision = evidenceAllowed ? PolicyDecision.AllowPublication : PolicyDecision.HumanOnly,
                ReasonCodesJson = JsonSerializer.Serialize(evidenceGates.Where(x => x.State != PolicyGateState.Pass).Select(x => x.ReasonCode)),
                EvaluatedAt = now
            };
            dbContext.PolicyEvaluations.Add(evidenceEvaluation);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (!evidenceAllowed || evidenceEvaluation.Decision != PolicyDecision.AllowPublication)
        {
            var reason = result.Classification == RepairClassification.RevisionUnverified
                ? NeedsHumanReason.RevisionUnverified
                : NeedsHumanReason.PolicyBlocked;
            await StopAttemptAsync(attempt, reason, "evidence-policy-blocked", cancellationToken);
            return HealingRepairCoordinatorStatus.Idle;
        }

        var gates = new[]
        {
            new Elsa.Platform.Healing.Abstractions.PolicyGateResult("persisted-result", PolicyGateState.Pass, "persisted-result-bound"),
            new Elsa.Platform.Healing.Abstractions.PolicyGateResult("evidence-policy", PolicyGateState.Pass, "evidence-policy-allowed"),
            new Elsa.Platform.Healing.Abstractions.PolicyGateResult("trusted-publisher", PolicyGateState.Pass, "publisher-revalidates-paths")
        };
        var evaluation = await dbContext.PolicyEvaluations.SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.AttemptId == attempt.Id &&
                 x.PolicyId == pathPolicy.Id &&
                 x.InputSnapshotHash == result.EnvelopeDigest,
            cancellationToken);
        if (evaluation is null)
        {
            evaluation = new PolicyEvaluation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = attempt.WorkspaceId,
                ApplicationId = attempt.ApplicationId,
                AttemptId = attempt.Id,
                PolicyId = pathPolicy.Id,
                PolicyKind = PolicyKind.Path,
                PolicyVersion = pathPolicy.PolicyVersion,
                PolicyHash = pathPolicy.PolicyHash,
                InputSnapshotHash = result.EnvelopeDigest,
                GateResultsJson = JsonSerializer.Serialize(gates),
                Decision = PolicyDecision.AllowPublication,
                ReasonCodesJson = "[\"publication-allowed\"]",
                EvaluatedAt = now
            };
            dbContext.PolicyEvaluations.Add(evaluation);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        var snapshot = new PolicyEvaluationSnapshot(
            HealingContractVersions.PolicyProtocol,
            evaluation.PolicyVersion,
            evaluation.PolicyHash,
            evaluation.InputSnapshotHash,
            PolicyDecisions.AllowPublication,
            gates,
            evaluation.EvaluatedAt);
        var publication = new RepairPublicationRequest(
            HealingContractVersions.ProviderProtocol,
            Repository(provider),
            attempt.IncidentId,
            attempt.EpisodeId,
            attempt.Id,
            binding.TargetBranch,
            attempt.TargetRevision,
            envelope,
            snapshot,
            $"publish:{attempt.Id:N}:{result.PatchDigest}");
        var enqueued = await providerOperations.EnqueueAsync(new ProviderOperationEnqueueRequest(
            attempt.WorkspaceId,
            attempt.ApplicationId,
            provider.Id,
            ProviderOperationKind.PublishPullRequest,
            publication.IdempotencyKey,
            JsonSerializer.Serialize(publication),
            attempt.IncidentId,
            attempt.Id), cancellationToken);
        if (!enqueued.IsReplay)
        {
            await dbContext.RepairAttempts.Where(x => x.Id == attempt.Id && x.Status == RepairAttemptStatus.ResultReceived)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, RepairAttemptStatus.Publishing), cancellationToken);
        }
        return enqueued.IsReplay ? HealingRepairCoordinatorStatus.Idle : HealingRepairCoordinatorStatus.PublicationQueued;
    }

    private static IReadOnlyList<Elsa.Platform.Healing.Abstractions.PolicyGateResult> EvaluateEvidencePolicy(
        EvidencePolicy policy,
        EvidenceBundle bundle,
        RepairResult result)
    {
        var reproductionPass = !policy.RequireReproduction || result.Classification == RepairClassification.Reproduced;
        var inferencePass = result.Classification != RepairClassification.InferredHighConfidence ||
                            policy.AllowHighConfidenceInference && result.Confidence >= policy.MinimumInferenceConfidence;
        var tierPass = bundle.Tier <= policy.MaximumTier;
        var permittedFields = ParseRevisions(policy.PermittedFieldsJson).ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> releasedFields;
        try
        {
            using var document = JsonDocument.Parse(bundle.CanonicalJson);
            releasedFields = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            releasedFields = new HashSet<string>(StringComparer.Ordinal) { "invalid" };
        }
        var fieldsPass = permittedFields.Count == 0 || releasedFields.All(permittedFields.Contains);
        return
        [
            Gate("reproduction", reproductionPass, reproductionPass ? "reproduction-policy-satisfied" : "reproduction-required"),
            Gate("inference", inferencePass, inferencePass ? "inference-policy-satisfied" : "inference-not-allowed"),
            Gate("evidence-tier", tierPass, tierPass ? "evidence-tier-allowed" : "evidence-tier-blocked"),
            Gate("evidence-fields", fieldsPass, fieldsPass ? "evidence-fields-allowed" : "evidence-field-blocked")
        ];
    }

    private static Elsa.Platform.Healing.Abstractions.PolicyGateResult Gate(string gate, bool pass, string reason) =>
        new(gate, pass ? PolicyGateState.Pass : PolicyGateState.Block, reason);

    private async ValueTask ReconcileFailedOperationsAsync(CancellationToken cancellationToken)
    {
        var failures = await dbContext.ProviderOperations.AsNoTracking()
            .Where(x => x.Status == ProviderOperationStatus.DeadLettered || x.Status == ProviderOperationStatus.Failed)
            .Where(x => x.OutcomeCode != "domain-failure-reconciled")
            .OrderBy(x => x.UpdatedAt)
            .Take(32)
            .ToArrayAsync(cancellationToken);
        foreach (var failure in failures)
        {
            if (failure.Kind == ProviderOperationKind.UpsertWorkItem && failure.IncidentId is not null)
            {
                await dbContext.RepairWorkItemProjections
                    .Where(x => x.IncidentId == failure.IncidentId && x.ProviderConnectionId == failure.ProviderConnectionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectionStatus, WorkItemProjectionStatus.Failed), cancellationToken);
            }
            else if (failure.AttemptId is { } attemptId)
            {
                var attempt = await dbContext.RepairAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId, cancellationToken);
                if (attempt is not null)
                    await StopAttemptAsync(attempt, NeedsHumanReason.PolicyBlocked, failure.OutcomeCode ?? "provider-operation-failed", cancellationToken);
            }
            await dbContext.ProviderOperations.Where(x => x.Id == failure.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OutcomeCode, "domain-failure-reconciled"), cancellationToken);
        }
    }

    private async ValueTask RecoverAbandonedAttemptsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dispatchDeadline = now.Subtract(RepairOrchestrationService.MaximumLeaseDuration);
        var abandoned = await dbContext.RepairAttempts.AsNoTracking()
            .Where(x => (x.Status == RepairAttemptStatus.Running && x.LeaseExpiresAt < now) ||
                        (x.Status == RepairAttemptStatus.ProposalReady && dbContext.ManagedRepairProposals.Any(proposal =>
                            proposal.AttemptId == x.Id && proposal.Status == ManagedRepairProposalStatus.Ready && proposal.ExpiresAt < now)) ||
                        (x.Status == RepairAttemptStatus.Dispatched && dbContext.ProviderOperations.Any(operation =>
                            operation.AttemptId == x.Id && operation.Kind == ProviderOperationKind.DispatchWorkflow &&
                            operation.Status == ProviderOperationStatus.Completed && operation.UpdatedAt < dispatchDeadline)))
            .OrderBy(x => x.Id)
            .Take(32)
            .ToArrayAsync(cancellationToken);
        foreach (var attempt in abandoned)
        {
            await dbContext.RepairAttempts.Where(x => x.Id == attempt.Id &&
                    (x.Status == RepairAttemptStatus.Running || x.Status == RepairAttemptStatus.ProposalReady ||
                     x.Status == RepairAttemptStatus.Dispatched))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, RepairAttemptStatus.Failed)
                    .SetProperty(x => x.OutcomeCode, "repair-attempt-abandoned")
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.LeaseOwner, (string?)null)
                    .SetProperty(x => x.LeaseToken, (string?)null)
                    .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
            await dbContext.ManagedRepairProposals.Where(x => x.AttemptId == attempt.Id && x.Status == ManagedRepairProposalStatus.Ready)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ManagedRepairProposalStatus.Expired)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
        }
    }

    private async ValueTask StopAttemptAsync(
        RepairAttempt attempt,
        NeedsHumanReason reason,
        string outcomeCode,
        CancellationToken cancellationToken)
    {
        await dbContext.RepairAttempts.Where(x => x.Id == attempt.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RepairAttemptStatus.Stopped)
                .SetProperty(x => x.OutcomeCode, outcomeCode)
                .SetProperty(x => x.CompletedAt, timeProvider.GetUtcNow()), cancellationToken);
        await MarkNeedsHumanAsync(attempt.IncidentId, reason, cancellationToken);
    }

    private ValueTask MarkNeedsHumanAsync(Guid incidentId, NeedsHumanReason reason, CancellationToken cancellationToken) =>
        new(dbContext.HealingIncidents.Where(x => x.Id == incidentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HealingIncidentStatus.NeedsHuman)
                .SetProperty(x => x.NeedsHumanReason, reason), cancellationToken));

    private RepairResultEnvelope Rehydrate(RepairResult result, RepairAttempt attempt)
    {
        var risk = JsonSerializer.Deserialize<RiskProjection>(result.RiskJson)
                   ?? throw new InvalidOperationException("The persisted repair risk projection is invalid.");
        return new RepairResultEnvelope(
            HealingContractVersions.AgentProtocol,
            attempt.Id,
            result.WorkflowRunId,
            result.WorkflowRunAttempt,
            result.BaseRevision,
            result.TargetRevision,
            Classification(result.Classification),
            result.Confidence,
            risk.CausalSummary,
            result.UnifiedDiff,
            result.PatchDigest,
            Deserialize<RepairChangedPathSuggestion[]>(result.ChangedPathsJson),
            Deserialize<RepairReproductionEvidence>(result.ReproductionJson),
            Deserialize<RepairRegressionEvidence>(result.RegressionJson),
            Deserialize<RepairValidationResult[]>(result.ValidationJson),
            risk.RiskSuggestions,
            risk.RollbackSummary,
            risk.Usage,
            risk.Timing,
            result.SubmittedAt);
    }

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidOperationException("A persisted repair projection is invalid.");
    private static string Classification(RepairClassification value) => value switch
    {
        RepairClassification.Reproduced => "reproduced",
        RepairClassification.InferredHighConfidence => "inferred-high-confidence",
        RepairClassification.RevisionUnverified => "revision-unverified",
        _ => "insufficient-confidence"
    };
    private static ProviderRepositoryReference Repository(ProviderConnection provider) =>
        new(provider.Id, provider.RepositoryProviderId, provider.RepositoryOwner, provider.RepositoryName);
    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool IsGitRevision(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);
    private static IReadOnlyList<string> ParseRevisions(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private sealed record RiskProjection(
        string CausalSummary,
        IReadOnlyList<string> RiskSuggestions,
        string RollbackSummary,
        RepairUsageSummary Usage,
        RepairTimingSummary Timing);
}
