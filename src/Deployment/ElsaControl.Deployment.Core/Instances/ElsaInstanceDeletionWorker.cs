using ElsaControl.Deployment.Abstractions.Instances;

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
        while (await store.TryClaimNextDeletionAsync(workerId.Trim(), _timeProvider.GetUtcNow(), cancellationToken) is { } item)
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
                providerInvocations++;
                var request = new ElsaInstanceCleanupRequest(item.Instance.WorkspaceId, item.Instance.Id,
                    item.Operation.Id, item.Operation.AttemptNumber, item.Instance.CurrentDeploymentReference,
                    item.Instance.PlacementAssignmentReference, item.Instance.ElsaTenantReference);
                request.Validate();
                try
                {
                    observation = await cleanupPort.CleanupAsync(request, cancellationToken);
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
            ElsaInstanceDeletionResult result;
            if (correlated && observation.Kind == ElsaInstanceCleanupObservationKind.ConfirmedAbsent)
            {
                var deletedAt = _timeProvider.GetUtcNow();
                result = await store.CommitDeletionAsync(new(
                    item.Instance.WorkspaceId, item.Instance.Id, item.Operation.Id, item.Outbox.Id,
                    item.Instance.Version, item.Operation.AttemptNumber, item.CorrelatedRunId, workerId.Trim(),
                    item.LeaseToken, item.LeaseVersion, fingerprint,
                    item.CanFinalizeLocally ? ElsaInstanceDeletionProofKind.LocalNoOwnedResources :
                        ElsaInstanceDeletionProofKind.ProviderConfirmedAbsent,
                    observation.DiagnosticCode,
                    observation.Evidence?.Reference, observation.Evidence?.Digest,
                    ElsaInstanceStateMachine.FinalizeDeletion(item.Instance, deletedAt),
                    item.Operation.TransitionTo(ElsaInstanceOperationState.Succeeded), deletedAt), cancellationToken);
            }
            else
            {
                var code = correlated ? observation.DiagnosticCode : "deletion.correlation.invalid";
                result = await store.RequireDeletionRecoveryAsync(new(
                    item.Instance.WorkspaceId, item.Instance.Id, item.Operation.Id, item.Outbox.Id,
                    item.Instance.Version, item.Operation.AttemptNumber, item.CorrelatedRunId, workerId.Trim(),
                    item.LeaseToken, item.LeaseVersion, fingerprint, code, _timeProvider.GetUtcNow()), cancellationToken);
            }

            results.Add(new(result.Outcome switch
            {
                ElsaInstanceDeletionOutcome.Deleted => ElsaInstanceLifecycleWorkerOutcome.Deleted,
                ElsaInstanceDeletionOutcome.AlreadyCompleted => ElsaInstanceLifecycleWorkerOutcome.AlreadyCompleted,
                ElsaInstanceDeletionOutcome.RecoveryRequired => ElsaInstanceLifecycleWorkerOutcome.Failed,
                _ => ElsaInstanceLifecycleWorkerOutcome.Conflict
            }, result.Operation, result.Instance, FailureCode: result.DiagnosticCode));
        }
        return new(results, providerInvocations);
    }
}
