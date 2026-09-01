using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Resumes accepted instance lifecycle work after the request transaction commits.
/// It resolves a safe immutable plan and asks its store to atomically persist that
/// plan, queue a run and reserve a target before optionally handing it to the
/// provider-neutral submission port.
/// </summary>
public sealed class ElsaInstanceLifecycleWorker(
    IElsaInstanceLifecycleWorkerStore store,
    IElsaInstancePlanResolver resolver,
    TimeProvider? timeProvider = null,
    IElsaInstanceProviderSubmissionPort? provider = null,
    IElsaInstanceProviderSubmissionStore? submissionStore = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IElsaInstanceProviderSubmissionPort? _provider = provider;
    private readonly IElsaInstanceProviderSubmissionStore? _submissionStore = submissionStore;

    public async Task<ElsaInstanceLifecycleWorkerBatchResult> ProcessAvailableAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Lifecycle worker identity is required.", nameof(workerId));

        var results = new List<ElsaInstanceLifecycleWorkerResult>();
        var providerInvocations = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await store.TryClaimNextAsync(workerId.Trim(), _timeProvider.GetUtcNow(), cancellationToken);
            if (item is null)
                break;

            // A malformed item is isolated to its operation. The queue must continue
            // so one corrupt record cannot starve later accepted work.
            var processed = await ProcessClaimedAsync(item, workerId.Trim(), cancellationToken);
            results.Add(processed.Result);
            providerInvocations += processed.ProviderInvocations;
        }

        return new ElsaInstanceLifecycleWorkerBatchResult(results, providerInvocations);
    }

    private async Task<(ElsaInstanceLifecycleWorkerResult Result, int ProviderInvocations)> ProcessClaimedAsync(
        ElsaInstanceLifecycleWorkItem item,
        string workerId,
        CancellationToken cancellationToken)
    {
        try
        {
            item.Validate();
            var resolution = await resolver.ResolveAsync(item.Resolution.PlanRequest, cancellationToken);
            if (!resolution.Succeeded)
                return (await FailAsync(item, workerId, "resolution.failed", cancellationToken), 0);

            if (resolution.Plan is null || resolution.Reference is null || resolution.CurrentResolvedRelease is null)
                return (await FailAsync(item, workerId, "resolution.invalid", cancellationToken), 0);

            var planJson = ResolvedElsaApplicationPlanSerialization.Serialize(resolution.Plan);
            var contentHash = ResolvedElsaApplicationPlanSerialization.ComputeContentHash(resolution.Plan);
            if (!string.Equals(contentHash, resolution.Reference.ContentHash, StringComparison.Ordinal) ||
                !Equals(resolution.CurrentResolvedRelease.PlanReference, resolution.Reference) ||
                !string.Equals(resolution.Reference.PlanId, item.Resolution.PlanRequest.PlanId, StringComparison.Ordinal) ||
                !string.Equals(resolution.Reference.PlanUri, item.Resolution.PlanRequest.PlanUri, StringComparison.Ordinal))
                return (await FailAsync(item, workerId, "resolution.invalid", cancellationToken), 0);

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
            var result = await store.CommitResolvedAsync(commit, cancellationToken);
            if (_provider is null || result.Outcome is not ElsaInstanceLifecycleWorkerOutcome.Queued)
                return (result, 0);

            // Submission happens only after the atomic plan/run reservation. The
            // provider seam is idempotent by operation identity, so an uncertain
            // process boundary cannot turn a replay into a second remote apply.
            var submission = new ElsaInstanceProviderSubmission(
                item.Outbox.WorkspaceId,
                item.Outbox.InstanceId,
                item.Outbox.OperationId,
                item.Operation.AttemptNumber,
                item.Instance.DesiredLifecycle,
                resolution.Plan,
                item.Resolution.DeploymentTarget,
                item.Instance.PlacementIntent.RegionCode);
            try
            {
                var submitted = await _provider.SubmitAsync(submission, cancellationToken);
                submitted.Validate();
                if (_submissionStore is not null)
                    await _submissionStore.CommitProviderSubmissionAsync(new(
                        submission.WorkspaceId,
                        submission.InstanceId,
                        submission.OperationId,
                        submission.AttemptNumber,
                        submitted.CorrelationId,
                        _timeProvider.GetUtcNow()), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // The durable queued run remains the source of truth. A provider
                // worker/reconciler may retry the same correlation; if the store
                // is available, it records an uncertain hand-off for explicit
                // recovery. This failure must never roll back a committed plan or
                // create a second run.
                if (_submissionStore is not null)
                {
                    try
                    {
                        await _submissionStore.CommitProviderSubmissionAsync(new(
                            submission.WorkspaceId,
                            submission.InstanceId,
                            submission.OperationId,
                            submission.AttemptNumber,
                            "provider-submission-uncertain",
                            _timeProvider.GetUtcNow()), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Preserve the committed queued run if the recovery
                        // marker itself loses a race with another worker.
                    }
                }
            }
            return (result, 1);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ElsaInstanceLifecycleConflictException)
        {
            return (Conflict(item), 0);
        }
        catch (Exception) when (item is not null)
        {
            return (await FailAsync(item, workerId, "resolution.invalid", cancellationToken), 0);
        }
    }

    private async Task<ElsaInstanceLifecycleWorkerResult> FailAsync(
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
        try
        {
            return await store.FailResolutionAsync(failure, cancellationToken);
        }
        catch (ElsaInstanceLifecycleConflictException)
        {
            return Conflict(item);
        }
    }

    private static ElsaInstanceLifecycleWorkerResult Conflict(ElsaInstanceLifecycleWorkItem item) =>
        new(
            ElsaInstanceLifecycleWorkerOutcome.Conflict,
            item.Operation,
            item.Instance,
            FailureCode: "lifecycle.claim.conflict",
            FailureSummary: "Lifecycle work item ownership changed before completion.");
}
