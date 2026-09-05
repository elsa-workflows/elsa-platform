using ElsaControl.Deployment.Abstractions.Instances;
using System.Security.Cryptography;
using System.Text;

namespace ElsaControl.Deployment.Core.Instances;

public sealed class ElsaInstanceDeletionWorker(
    IElsaInstanceDeletionStore store,
    IElsaInstanceProviderCleanupPort cleanupPort,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElsaInstanceLifecycleWorkerBatchResult> ProcessAvailableAsync(string workerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Deletion worker identity is required.", nameof(workerId));
        var results = new List<ElsaInstanceLifecycleWorkerResult>();
        var providerInvocations = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await store.TryClaimNextDeletionAsync(workerId.Trim(), _timeProvider.GetUtcNow(), cancellationToken);
            if (item is null)
                break;
            try
            {
                var processed = await ProcessClaimedAsync(
                    item, workerId.Trim(), () => providerInvocations++, cancellationToken);
                if (processed is not null)
                    results.Add(Map(processed));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ElsaInstanceLifecycleConflictException)
            {
                if (TryConflict(item) is { } conflict)
                    results.Add(conflict);
            }
            catch (Exception)
            {
                var failure = TryInvalidFailure(item, workerId.Trim());
                if (failure is null)
                    continue;
                try
                {
                    results.Add(Map(await store.RequireDeletionRecoveryAsync(failure, cancellationToken)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (TryConflict(item) is { } conflict)
                        results.Add(conflict);
                }
            }
        }
        return new(results, providerInvocations);
    }

    private async Task<ElsaInstanceDeletionResult?> ProcessClaimedAsync(
        ElsaInstanceDeletionWorkItem item,
        string workerId,
        Action providerInvoked,
        CancellationToken cancellationToken)
    {
        item.Validate();
        ElsaInstanceCleanupObservation observation;
        if (item.CanFinalizeLocally)
        {
            observation = new(ElsaInstanceCleanupObservationKind.ConfirmedAbsent, item.Operation.Id,
                item.Operation.AttemptNumber, "deletion.local.absent");
        }
        else
        {
            var request = new ElsaInstanceCleanupRequest(item.Instance.WorkspaceId, item.Instance.Id,
                item.Operation.Id, item.Operation.AttemptNumber, item.Instance.CurrentDeploymentReference,
                item.Instance.PlacementAssignmentReference, item.Instance.ElsaTenantReference);
            request.Validate();
            try
            {
                providerInvoked();
                observation = await CleanupWithLeaseAsync(item, workerId, request, cancellationToken);
                observation.Validate();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                observation = new(ElsaInstanceCleanupObservationKind.Unavailable, item.Operation.Id,
                    item.Operation.AttemptNumber, "deletion.provider.unavailable");
            }
        }

        var correlated = observation.OperationId == item.Operation.Id &&
                         observation.AttemptNumber == item.Operation.AttemptNumber;
        var fingerprint = observation.ComputeFingerprint();
        if (correlated && observation.Kind == ElsaInstanceCleanupObservationKind.InProgress)
        {
            if (!await store.DeferDeletionAsync(item, workerId, _timeProvider.GetUtcNow(),
                    observation.DiagnosticCode, cancellationToken))
                throw new ElsaInstanceLifecycleConflictException(
                    "Deletion work item changed before async cleanup could be deferred.");
            return null;
        }
        if (correlated && observation.Kind == ElsaInstanceCleanupObservationKind.ConfirmedAbsent)
        {
            var deletedAt = _timeProvider.GetUtcNow();
            var result = await store.CommitDeletionAsync(new(
                item.Instance.WorkspaceId, item.Instance.Id, item.Operation.Id, item.Outbox.Id,
                item.Instance.Version, item.Operation.AttemptNumber, item.CorrelatedRunId, workerId,
                item.LeaseToken, item.LeaseVersion, fingerprint,
                item.CanFinalizeLocally ? ElsaInstanceDeletionProofKind.LocalNoOwnedResources :
                    ElsaInstanceDeletionProofKind.ProviderConfirmedAbsent,
                observation.DiagnosticCode,
                observation.Evidence?.Reference, observation.Evidence?.Digest,
                ElsaInstanceStateMachine.FinalizeDeletion(item.Instance, deletedAt),
                item.Operation.TransitionTo(ElsaInstanceOperationState.Succeeded), deletedAt), cancellationToken);
            return result;
        }

        var code = correlated ? observation.DiagnosticCode : "deletion.correlation.invalid";
        return await store.RequireDeletionRecoveryAsync(new(
            item.Instance.WorkspaceId, item.Instance.Id, item.Operation.Id, item.Outbox.Id,
            item.Instance.Version, item.Operation.AttemptNumber, item.CorrelatedRunId, workerId,
            item.LeaseToken, item.LeaseVersion, fingerprint, code, _timeProvider.GetUtcNow()), cancellationToken);
    }

    private ElsaInstanceDeletionFailure? TryInvalidFailure(ElsaInstanceDeletionWorkItem item, string workerId)
    {
        if (item.Outbox is null || item.Operation is null || item.Instance is null ||
            item.Outbox.Id == Guid.Empty || item.Operation.Id == Guid.Empty || item.Instance.Id == Guid.Empty ||
            item.Instance.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(item.LeaseToken) || item.LeaseVersion < 1)
            return null;
        var canonical = $"deletion.item.invalid\n{item.Outbox.Id:D}\n{item.Operation.Id:D}\n{item.Instance.Id:D}\n";
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(item.Instance.WorkspaceId, item.Instance.Id, item.Operation.Id, item.Outbox.Id,
            Math.Max(1, item.Instance.Version), Math.Max(1, item.Operation.AttemptNumber), item.CorrelatedRunId,
            workerId, item.LeaseToken, Math.Max(1, item.LeaseVersion), fingerprint,
            "deletion.item.invalid", _timeProvider.GetUtcNow());
    }

    private async Task<ElsaInstanceCleanupObservation> CleanupWithLeaseAsync(
        ElsaInstanceDeletionWorkItem item,
        string workerId,
        ElsaInstanceCleanupRequest request,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cleanup = cleanupPort.CleanupAsync(request, cancellation.Token);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _timeProvider);
        while (await Task.WhenAny(cleanup, timer.WaitForNextTickAsync(cancellationToken).AsTask()) != cleanup)
        {
            if (!await store.RenewDeletionLeaseAsync(item, workerId, _timeProvider.GetUtcNow(), cancellationToken))
            {
                cancellation.Cancel();
                if (!cleanup.IsCompleted)
                    _ = cleanup.ContinueWith(static task => _ = task.Exception,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                throw new InvalidOperationException("Deletion lease could not be renewed.");
            }
        }
        return await cleanup;
    }

    private static ElsaInstanceLifecycleWorkerResult Map(ElsaInstanceDeletionResult result) => new(
        result.Outcome switch
        {
            ElsaInstanceDeletionOutcome.Deleted => ElsaInstanceLifecycleWorkerOutcome.Deleted,
            ElsaInstanceDeletionOutcome.AlreadyCompleted => ElsaInstanceLifecycleWorkerOutcome.AlreadyCompleted,
            ElsaInstanceDeletionOutcome.RecoveryRequired => ElsaInstanceLifecycleWorkerOutcome.Failed,
            _ => ElsaInstanceLifecycleWorkerOutcome.Conflict
        }, result.Operation, result.Instance, FailureCode: result.DiagnosticCode);

    private static ElsaInstanceLifecycleWorkerResult? TryConflict(ElsaInstanceDeletionWorkItem item) =>
        item.Operation is null || item.Instance is null ? null : new(
            ElsaInstanceLifecycleWorkerOutcome.Conflict,
            item.Operation,
            item.Instance,
            FailureCode: "deletion.claim.conflict",
            FailureSummary: "Deletion work item ownership changed before completion.");
}
