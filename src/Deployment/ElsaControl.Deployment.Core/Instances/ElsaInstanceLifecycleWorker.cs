using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Resumes accepted instance lifecycle work after the request transaction commits.
/// The worker never invokes a provider: it only resolves a safe immutable plan and
/// asks its store to atomically persist that plan, queue a run, and reserve a target.
/// </summary>
public sealed class ElsaInstanceLifecycleWorker(
    IElsaInstanceLifecycleWorkerStore store,
    IElsaInstancePlanResolver resolver,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElsaInstanceLifecycleWorkerBatchResult> ProcessAvailableAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Lifecycle worker identity is required.", nameof(workerId));

        var results = new List<ElsaInstanceLifecycleWorkerResult>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await store.TryClaimNextAsync(workerId.Trim(), _timeProvider.GetUtcNow(), cancellationToken);
            if (item is null)
                break;

            // A malformed item is isolated to its operation. The queue must continue
            // so one corrupt record cannot starve later accepted work.
            results.Add(await ProcessClaimedAsync(item, workerId.Trim(), cancellationToken));
        }

        return new ElsaInstanceLifecycleWorkerBatchResult(results, ProviderInvocations: 0);
    }

    private async Task<ElsaInstanceLifecycleWorkerResult> ProcessClaimedAsync(
        ElsaInstanceLifecycleWorkItem item,
        string workerId,
        CancellationToken cancellationToken)
    {
        try
        {
            item.Validate();
            var resolution = await resolver.ResolveAsync(item.Resolution.PlanRequest, cancellationToken);
            if (!resolution.Succeeded)
                return await FailAsync(item, workerId, "resolution.failed", cancellationToken);

            if (resolution.Plan is null || resolution.Reference is null || resolution.CurrentResolvedRelease is null)
                return await FailAsync(item, workerId, "resolution.invalid", cancellationToken);

            var planJson = ResolvedElsaApplicationPlanSerialization.Serialize(resolution.Plan);
            var contentHash = ResolvedElsaApplicationPlanSerialization.ComputeContentHash(resolution.Plan);
            if (!string.Equals(contentHash, resolution.Reference.ContentHash, StringComparison.Ordinal) ||
                !Equals(resolution.CurrentResolvedRelease.PlanReference, resolution.Reference) ||
                !string.Equals(resolution.Reference.PlanId, item.Resolution.PlanRequest.PlanId, StringComparison.Ordinal) ||
                !string.Equals(resolution.Reference.PlanUri, item.Resolution.PlanRequest.PlanUri, StringComparison.Ordinal))
                return await FailAsync(item, workerId, "resolution.invalid", cancellationToken);

            var resolvedInstance = item.Instance.AttachResolvedPlan(
                resolution.Reference,
                resolution.CurrentResolvedRelease);
            var queuedOperation = item.Operation.TransitionTo(
                ElsaControl.Deployment.Abstractions.Instances.ElsaInstanceOperationState.Queued);
            var commit = new ElsaInstanceLifecycleResolutionCommit(
                item.Outbox.WorkspaceId,
                item.Outbox.InstanceId,
                item.Outbox.OperationId,
                item.Outbox.Id,
                item.Outbox.RequestHash,
                workerId,
                queuedOperation,
                resolvedInstance,
                new ElsaInstanceLifecycleResolvedPlan(resolution.Reference, planJson),
                item.Resolution.DeploymentTarget,
                _timeProvider.GetUtcNow(),
                item.LeaseToken,
                item.LeaseVersion);
            return await store.CommitResolvedAsync(commit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (item is not null)
        {
            return await FailAsync(item, workerId, "resolution.invalid", cancellationToken);
        }
    }

    private Task<ElsaInstanceLifecycleWorkerResult> FailAsync(
        ElsaInstanceLifecycleWorkItem item,
        string workerId,
        string code,
        CancellationToken cancellationToken)
    {
        var failure = new ElsaInstanceLifecycleResolutionFailure(
            item.Outbox.WorkspaceId,
            item.Outbox.InstanceId,
            item.Outbox.OperationId,
            item.Outbox.Id,
            item.Outbox.RequestHash,
            workerId,
            code,
            code == "resolution.failed"
                ? "Lifecycle plan resolution was rejected."
                : "Lifecycle work item could not be resolved safely.",
            _timeProvider.GetUtcNow(),
            item.LeaseToken,
            item.LeaseVersion);
        return store.FailResolutionAsync(failure, cancellationToken);
    }
}
