using System.Text.Json;
using System.Security.Cryptography;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Providers;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.Healing.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Platform.Api.Healing;

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
        var projectionAuthority = await dbContext.RepairWorkItemProjections.AsNoTracking().SingleOrDefaultAsync(
            x => x.IncidentId == request.IncidentId && x.EpisodeId == request.EpisodeId, cancellationToken);
        if (projectionAuthority is null || !await authorityService.CanMutateAsync(
                operation.WorkspaceId, operation.ApplicationId, request.EpisodeId, operation.ProviderConnectionId,
                request.IncidentId, null, cancellationToken))
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
    private readonly IDataProtector _nonceProtector = dataProtectionProvider.CreateProtector("Elsa.Platform.Healing.DispatchNonce.v1");
    public ProviderOperationKind Kind => ProviderOperationKind.DispatchWorkflow;

    public async ValueTask<HealingOperationOutcome> ExecuteAsync(
        ProviderOperation operation,
        CancellationToken cancellationToken = default)
    {
        var protectedRequest = ProviderOperationPayload.Deserialize<RepairWorkflowDispatchRequest>(operation);
        if (!await authorityService.CanMutateAsync(
                operation.WorkspaceId, operation.ApplicationId, protectedRequest.EpisodeId, operation.ProviderConnectionId,
                protectedRequest.IncidentId, protectedRequest.AttemptId, cancellationToken))
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
        if (!await authorityService.CanMutateAsync(
                operation.WorkspaceId, operation.ApplicationId, request.EpisodeId, operation.ProviderConnectionId,
                request.IncidentId, request.AttemptId, cancellationToken))
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
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
            x => x.WorkspaceId == operation.WorkspaceId &&
                 x.ApplicationId == operation.ApplicationId &&
                 x.Id == request.AttemptId,
            cancellationToken);
        if (attempt is null)
            return HealingOperationOutcome.DeadLettered("repair-attempt-not-found");

        var pullRequest = await dbContext.RepairPullRequests.SingleOrDefaultAsync(
            x => x.AttemptId == request.AttemptId,
            cancellationToken);
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
        pullRequest.Branch = $"elsa-healing/{request.AttemptId:N}";
        pullRequest.BaseRevision = reference.BaseRevision;
        pullRequest.HeadRevision = reference.HeadRevision;
        pullRequest.PatchDigest = request.Result.PatchDigest;
        pullRequest.IsDraft = reference.IsDraft;
        pullRequest.Classification = attempt.RepairClassification;
        pullRequest.MergeState = PullRequestMergeState.Open;
        attempt.Status = RepairAttemptStatus.PullRequestOpen;
        await dbContext.HealingIncidents.Where(x => x.Id == request.IncidentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HealingIncidentStatus.PullRequestOpen)
                .SetProperty(x => x.NeedsHumanReason, (NeedsHumanReason?)null), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return HealingOperationOutcome.Completed("repair-pull-request-published");
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
}
