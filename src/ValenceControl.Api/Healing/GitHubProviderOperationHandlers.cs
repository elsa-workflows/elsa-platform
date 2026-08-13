using System.Text.Json;
using System.Security.Cryptography;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Operations;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.Healing.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ValenceControl.Healing.Core.Configuration;

namespace ValenceControl.Api.Healing;

public sealed class GitHubUpsertWorkItemOperationHandler(
    IRepairWorkProvider provider,
    HealingDbContext dbContext,
    TimeProvider timeProvider,
    HealingRepairAuthorityService authorityService) : IProviderOperationHandler
{
    public ProviderOperationKind Kind => ProviderOperationKind.UpsertWorkItem;

    public async ValueTask<HealingOperationOutcome> ExecuteAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        var request = ProviderOperationPayload.Deserialize<RepairWorkItemUpsertRequest>(operation);
        if (!await ProviderOperationPayload.RepositoryMatchesAsync(operation, request.Repository, dbContext, cancellationToken))
            return HealingOperationOutcome.DeadLettered("repository-authority-mismatch");
        var projectionAuthority = await dbContext.RepairWorkItemProjections.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ApplicationId == operation.ApplicationId &&
                 x.ProviderConnectionId == operation.ProviderConnectionId &&
                 x.IncidentId == request.IncidentId && x.EpisodeId == request.EpisodeId, cancellationToken);
        if (projectionAuthority is null || !await authorityService.CanMutateAsync(
                operation.WorkspaceId, operation.ApplicationId, request.EpisodeId, operation.ProviderConnectionId,
                request.IncidentId, cancellationToken))
            return HealingOperationOutcome.DeadLettered("healing-authority-revoked");
        ProviderWorkItemReference reference;
        try
        {
            reference = await provider.UpsertWorkItemAsync(request, cancellationToken);
        }
        catch (GitHubSecurityException exception) when (
            exception.ReasonCode == GitHubSecurityReasonCodes.OperationInProgress)
        {
            return HealingOperationOutcome.Retry("provider-operation-reservation-active");
        }
        var projection = await dbContext.RepairWorkItemProjections.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ApplicationId == operation.ApplicationId &&
                 x.IncidentId == request.IncidentId &&
                 x.EpisodeId == request.EpisodeId &&
                 x.ProviderConnectionId == operation.ProviderConnectionId,
            cancellationToken);
        if (projection is null)
            return HealingOperationOutcome.DeadLettered("work-item-projection-not-found");

        projection.ProviderWorkItemId = reference.ProviderWorkItemId;
        projection.Number = reference.Number;
        projection.Url = reference.Url.ToString();
        projection.ProviderState = reference.State;
        projection.MachineSummaryHash = request.MachineSummaryHash;
        projection.ProjectionStatus = WorkItemProjectionStatus.Current;
        projection.LastProjectedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return HealingOperationOutcome.Completed("work-item-projected");
    }
}

public sealed class GitHubDispatchWorkflowOperationHandler(
    IRepairWorkProvider provider,
    HealingDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    HealingRepairAuthorityService authorityService) : IProviderOperationHandler
{
    private readonly IDataProtector _nonceProtector = dataProtectionProvider.CreateProtector("ValenceControl.Healing.DispatchNonce.v1");
    public ProviderOperationKind Kind => ProviderOperationKind.DispatchWorkflow;

    public async ValueTask<HealingOperationOutcome> ExecuteAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        var protectedRequest = ProviderOperationPayload.Deserialize<RepairWorkflowDispatchRequest>(operation);
        if (!await ProviderOperationPayload.RepositoryMatchesAsync(operation, protectedRequest.Repository, dbContext, cancellationToken))
            return HealingOperationOutcome.DeadLettered("repository-authority-mismatch");
        if (!await authorityService.CanMutateAttemptAsync(
                operation.WorkspaceId, operation.ApplicationId, protectedRequest.EpisodeId, operation.ProviderConnectionId,
                protectedRequest.AttemptId, RepairAttemptStatus.Queued, cancellationToken))
            return HealingOperationOutcome.DeadLettered("healing-authority-revoked");
        if (!protectedRequest.OneTimeNonce.StartsWith("dp:", StringComparison.Ordinal))
            return HealingOperationOutcome.DeadLettered("dispatch-nonce-not-protected");
        RepairWorkflowDispatchRequest request;
        try
        {
            request = protectedRequest with { OneTimeNonce = _nonceProtector.Unprotect(protectedRequest.OneTimeNonce[3..]) };
        }
        catch (CryptographicException)
        {
            return HealingOperationOutcome.DeadLettered("dispatch-nonce-unprotect-failed");
        }
        try
        {
            await provider.DispatchWorkflowAsync(request, cancellationToken);
        }
        catch (GitHubSecurityException exception) when (
            exception.ReasonCode == GitHubSecurityReasonCodes.OperationInProgress)
        {
            return HealingOperationOutcome.Retry("provider-operation-reservation-active");
        }
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ApplicationId == operation.ApplicationId &&
                 x.Id == request.AttemptId,
            cancellationToken);
        if (attempt is null)
            return HealingOperationOutcome.DeadLettered("repair-attempt-not-found");
        if (attempt.Status == RepairAttemptStatus.Queued)
        {
            attempt.Status = RepairAttemptStatus.Dispatched;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return HealingOperationOutcome.Completed("repair-workflow-dispatched");
    }
}

public sealed class GitHubPublishPullRequestOperationHandler(
    ITrustedPatchPublisher publisher,
    HealingDbContext dbContext,
    HealingRepairAuthorityService authorityService) : IProviderOperationHandler
{
    public ProviderOperationKind Kind => ProviderOperationKind.PublishPullRequest;

    public async ValueTask<HealingOperationOutcome> ExecuteAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        var request = ProviderOperationPayload.Deserialize<RepairPublicationRequest>(operation);
        if (!await ProviderOperationPayload.RepositoryMatchesAsync(operation, request.Repository, dbContext, cancellationToken))
            return HealingOperationOutcome.DeadLettered("repository-authority-mismatch");
        if (!await authorityService.CanMutateAttemptAsync(
                operation.WorkspaceId, operation.ApplicationId, request.EpisodeId, operation.ProviderConnectionId,
                request.AttemptId, RepairAttemptStatus.Publishing, cancellationToken))
            return HealingOperationOutcome.DeadLettered("healing-authority-revoked");
        ProviderPullRequestReference reference;
        try
        {
            reference = await publisher.PublishAsync(request, cancellationToken);
        }
        catch (GitHubSecurityException exception) when (
            exception.ReasonCode == GitHubSecurityReasonCodes.OperationInProgress)
        {
            return HealingOperationOutcome.Retry("provider-operation-reservation-active");
        }
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var authorityStillCurrent = await authorityService.CanMutateAttemptAsync(
            operation.WorkspaceId,
            operation.ApplicationId,
            request.EpisodeId,
            operation.ProviderConnectionId,
            request.AttemptId,
            RepairAttemptStatus.Publishing,
            cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ApplicationId == operation.ApplicationId &&
                 x.Id == request.AttemptId,
            cancellationToken);
        if (attempt is null)
            return HealingOperationOutcome.DeadLettered("repair-attempt-not-found");

        var pullRequest = await dbContext.RepairPullRequests.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ApplicationId == operation.ApplicationId &&
                 x.ProviderConnectionId == operation.ProviderConnectionId &&
                 x.AttemptId == request.AttemptId,
            cancellationToken);
        if (pullRequest?.MergeState is PullRequestMergeState.Merged or PullRequestMergeState.Closed)
            return HealingOperationOutcome.Completed("publication-operation-superseded");
        if (pullRequest is null)
        {
            pullRequest = new RepairPullRequest
            {
                Id = Guid.NewGuid(),
                WorkspaceId = operation.WorkspaceId,
                ApplicationId = operation.ApplicationId,
                AttemptId = request.AttemptId,
                ProviderConnectionId = operation.ProviderConnectionId,
                CheckSnapshotJson = "{}",
                BranchProtectionSnapshotJson = "{}"
            };
            dbContext.RepairPullRequests.Add(pullRequest);
        }
        pullRequest.ProviderPullRequestId = reference.ProviderPullRequestId;
        pullRequest.Number = reference.Number;
        pullRequest.Url = reference.Url.ToString();
        pullRequest.Branch = $"valence-control-healing/{request.AttemptId:N}";
        pullRequest.BaseRevision = reference.BaseRevision;
        pullRequest.HeadRevision = reference.HeadRevision;
        pullRequest.PatchDigest = request.Result.PatchDigest;
        pullRequest.IsDraft = reference.IsDraft;
        pullRequest.Classification = attempt.RepairClassification;
        pullRequest.Version = Guid.NewGuid().ToByteArray();
        if (authorityStillCurrent && attempt.Status == RepairAttemptStatus.Publishing)
        {
            pullRequest.MergeState = PullRequestMergeState.Open;
            attempt.Status = RepairAttemptStatus.PullRequestOpen;
            attempt.Version = Guid.NewGuid().ToByteArray();
            await dbContext.HealingIncidents.Where(x =>
                    x.WorkspaceId == operation.WorkspaceId &&
                    x.ApplicationId == operation.ApplicationId &&
                    x.Id == request.IncidentId &&
                    x.ActiveEpisodeId == request.EpisodeId &&
                    (x.Status == HealingIncidentStatus.Repairing || x.Status == HealingIncidentStatus.PullRequestOpen) &&
                    dbContext.IncidentEpisodes.Any(episode =>
                        episode.WorkspaceId == x.WorkspaceId &&
                        episode.ApplicationId == x.ApplicationId &&
                        episode.Id == request.EpisodeId &&
                        episode.IncidentId == x.Id &&
                        episode.Outcome == IncidentEpisodeOutcome.Active))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, HealingIncidentStatus.PullRequestOpen)
                    .SetProperty(x => x.NeedsHumanReason, (NeedsHumanReason?)null)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return HealingOperationOutcome.Completed(
            authorityStillCurrent ? "repair-pull-request-published" : "repair-pull-request-published-stale");
    }
}

public sealed class GitHubRequestMergeOperationHandler(
    IRepairMergeProvider provider,
    HealingDbContext dbContext,
    HealingRepairAuthorityService authorityService,
    ITrustedDeploymentSafetyCapabilitySource deploymentSafetyCapabilities,
    IOptions<HealingOptions> options) : IProviderOperationHandler
{
    public ProviderOperationKind Kind => ProviderOperationKind.RequestMerge;

    public async ValueTask<HealingOperationOutcome> ExecuteAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        var request = ProviderOperationPayload.Deserialize<ProviderMergeRequest>(operation);
        if (!await ProviderOperationPayload.RepositoryMatchesAsync(operation, request.Repository, dbContext, cancellationToken))
            return HealingOperationOutcome.DeadLettered("repository-authority-mismatch");
        var attempt = operation.AttemptId.HasValue
            ? await dbContext.RepairAttempts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.WorkspaceId == operation.WorkspaceId && x.ApplicationId == operation.ApplicationId &&
                x.Id == operation.AttemptId.Value, cancellationToken)
            : null;
        if (attempt is null)
            return HealingOperationOutcome.DeadLettered("repair-attempt-not-found");
        var pullRequest = await dbContext.RepairPullRequests.SingleOrDefaultAsync(x =>
            x.WorkspaceId == operation.WorkspaceId && x.ApplicationId == operation.ApplicationId &&
            x.AttemptId == attempt.Id && x.ProviderConnectionId == operation.ProviderConnectionId,
            cancellationToken);
        if (pullRequest is null)
            return HealingOperationOutcome.DeadLettered("repair-pull-request-not-found");
        if (pullRequest.MergeState is PullRequestMergeState.Merged or PullRequestMergeState.Closed ||
            attempt.Status is RepairAttemptStatus.Succeeded or RepairAttemptStatus.Stopped)
            return HealingOperationOutcome.Completed("merge-operation-superseded");
        if (pullRequest.MergePolicyEvaluationId is null ||
            pullRequest.MergeState != PullRequestMergeState.MergeRequested)
            return HealingOperationOutcome.Completed("merge-operation-superseded");
        if (pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) != request.PullRequestId ||
            pullRequest.HeadRevision != request.ExpectedHeadRevision)
            return HealingOperationOutcome.Completed("merge-operation-superseded");
        if (!await authorityService.CanMutateAttemptAsync(
                operation.WorkspaceId, operation.ApplicationId, attempt.EpisodeId,
                operation.ProviderConnectionId, attempt.Id, RepairAttemptStatus.PullRequestOpen, cancellationToken))
            return await InvalidateAsync(pullRequest, "healing-authority-revoked", cancellationToken);
        var evaluation = await dbContext.PolicyEvaluations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == pullRequest.MergePolicyEvaluationId && x.WorkspaceId == operation.WorkspaceId &&
            x.ApplicationId == operation.ApplicationId && x.AttemptId == attempt.Id,
            cancellationToken);
        var policy = evaluation is null ? null : await dbContext.MergePolicies.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == evaluation.PolicyId && x.WorkspaceId == operation.WorkspaceId &&
            x.ApplicationId == operation.ApplicationId && x.PolicyHash == evaluation.PolicyHash,
            cancellationToken);
        var application = await dbContext.HealingConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.WorkspaceId == operation.WorkspaceId && x.ApplicationId == operation.ApplicationId, cancellationToken);
        var workspace = await dbContext.HealingWorkspaceConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.WorkspaceId == operation.WorkspaceId, cancellationToken);
        if (evaluation?.Decision != PolicyDecision.AllowAutomaticMerge || policy is null || application is null || workspace is null ||
            !options.Value.AutomaticMergeEnabled || options.Value.ControlKillSwitch ||
            !application.AutomaticMergeEnabled || application.ApplicationKillSwitch || workspace.WorkspaceKillSwitch)
            return await ResetAsync(pullRequest, "merge-policy-changed", cancellationToken);

        var deploymentSafety = await deploymentSafetyCapabilities.GetAsync(
            operation.WorkspaceId,
            operation.ApplicationId,
            attempt.EpisodeId,
            cancellationToken);
        if (deploymentSafety.State != RepairPolicyObservationState.Satisfied)
            return await ResetAsync(pullRequest, "deployment-safety-changed", cancellationToken);

        var snapshot = await provider.GetMergeSnapshotAsync(request.Repository, request.PullRequestId, cancellationToken);
        if (!snapshot.IsOpen)
        {
            // A merge may have succeeded while the durable operation still held its lease. The signed pull-request
            // webhook owns the canonical merged/closed transition, so never roll that state back from this stale command.
            return HealingOperationOutcome.Completed("merge-provider-terminal-observed");
        }
        var configuredChecks = ParseStrings(policy.RequiredChecksJson);
        var checksPass = configuredChecks.All(required => snapshot.RequiredChecks.Contains(required, StringComparer.Ordinal) && snapshot.Checks.Any(x =>
            x.Name == required && x.State.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            x.Revision == request.ExpectedHeadRevision));
        var verifierPass = !string.IsNullOrWhiteSpace(policy.IndependentVerifier) &&
            snapshot.RequiredChecks.Contains(policy.IndependentVerifier, StringComparer.Ordinal) && snapshot.Checks.Any(x =>
            x.Name == policy.IndependentVerifier && x.State.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            x.Revision == request.ExpectedHeadRevision);
        if (snapshot.IsDraft || snapshot.HeadRevision != request.ExpectedHeadRevision ||
            snapshot.BaseRevision != pullRequest.BaseRevision || !snapshot.IsBranchProtectionSatisfied ||
            !checksPass || !verifierPass)
            return await ResetAsync(pullRequest, "provider-merge-constraints-changed", cancellationToken);
        try
        {
            await provider.RequestMergeAsync(request, cancellationToken);
        }
        catch (GitHubSecurityException exception) when (exception.ReasonCode == GitHubSecurityReasonCodes.OperationInProgress)
        {
            return HealingOperationOutcome.Retry("provider-operation-reservation-active");
        }
        return HealingOperationOutcome.Completed("repair-merge-requested");
    }

    private async ValueTask<HealingOperationOutcome> ResetAsync(
        RepairPullRequest pullRequest,
        string outcome,
        CancellationToken cancellationToken)
    {
        pullRequest.MergeState = PullRequestMergeState.Open;
        pullRequest.MergePolicyEvaluationId = null;
        pullRequest.Version = Guid.NewGuid().ToByteArray();
        await dbContext.SaveChangesAsync(cancellationToken);
        return HealingOperationOutcome.Completed(outcome);
    }

    private async ValueTask<HealingOperationOutcome> InvalidateAsync(
        RepairPullRequest pullRequest,
        string outcome,
        CancellationToken cancellationToken)
    {
        pullRequest.MergeState = PullRequestMergeState.Open;
        pullRequest.MergePolicyEvaluationId = null;
        pullRequest.ClosureReason = outcome;
        pullRequest.Version = Guid.NewGuid().ToByteArray();
        await dbContext.SaveChangesAsync(cancellationToken);
        return HealingOperationOutcome.DeadLettered(outcome);
    }

    private static IReadOnlySet<string> ParseStrings(string json)
    {
        try { return (JsonSerializer.Deserialize<string[]>(json) ?? []).ToHashSet(StringComparer.Ordinal); }
        catch (JsonException) { return new HashSet<string>(StringComparer.Ordinal); }
    }
}

internal static class ProviderOperationPayload
{
    public static T Deserialize<T>(ProviderOperation operation)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(operation.PayloadJson)
                   ?? throw new JsonException("The provider operation payload was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The provider operation payload is invalid.", exception);
        }
    }

    public static async ValueTask<bool> RepositoryMatchesAsync(
        ProviderOperation operation,
        ProviderRepositoryReference repository,
        HealingDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (repository.ProviderConnectionId != operation.ProviderConnectionId)
            return false;
        return await dbContext.ProviderConnections.AsNoTracking().AnyAsync(x =>
            x.WorkspaceId == operation.WorkspaceId &&
            x.Id == operation.ProviderConnectionId &&
            x.RepositoryProviderId == repository.RepositoryProviderId &&
            x.RepositoryOwner == repository.Owner &&
            x.RepositoryName == repository.Name,
            cancellationToken);
    }
}
