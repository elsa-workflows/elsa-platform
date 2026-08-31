using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Deterministic recording store for core tests and local composition. Its lock
/// models the atomic boundary required of the relational implementation without
/// introducing persistence or provider concerns into the lifecycle service.
/// </summary>
public sealed class InMemoryElsaInstanceLifecycleStore(TimeProvider? timeProvider = null) : IElsaInstanceLifecycleStore, IElsaInstanceLifecycleWorkerStore, IElsaInstanceProviderReconciliationStore, IElsaInstanceDeletionStore
{
    private static readonly TimeSpan WorkerLeaseDuration = TimeSpan.FromMinutes(5);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ElsaInstance> _instances = [];
    private readonly Dictionary<Guid, ElsaInstanceOperation> _operations = [];
    private readonly Dictionary<Guid, ElsaInstanceLifecycleOutboxMessage> _outbox = [];
    private readonly Dictionary<Guid, ElsaInstanceLifecycleResolutionInput> _resolutionInputs = [];
    private readonly Dictionary<Guid, LifecycleClaim> _claims = [];
    private readonly Dictionary<string, ElsaInstanceLifecycleResolvedPlan> _resolvedPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, ElsaInstanceLifecycleDeploymentRun> _deploymentRuns = [];
    private readonly Dictionary<Guid, ElsaInstanceLifecycleRecordedFailure> _failures = [];
    private readonly Dictionary<Guid, StoredReconciliationResult> _reconciliationResults = [];
    private readonly Dictionary<Guid, StoredDeletionResult> _deletionResults = [];

    public IReadOnlyCollection<ElsaInstance> Instances
    {
        get
        {
            lock (_gate)
                return _instances.Values.ToArray();
        }
    }

    public IReadOnlyCollection<ElsaInstanceOperation> Operations
    {
        get
        {
            lock (_gate)
                return _operations.Values.ToArray();
        }
    }

    public IReadOnlyCollection<ElsaInstanceLifecycleOutboxMessage> Outbox
    {
        get
        {
            lock (_gate)
                return _outbox.Values.ToArray();
        }
    }

    public IReadOnlyCollection<ElsaInstanceLifecycleDeploymentRun> DeploymentRuns
    {
        get
        {
            lock (_gate)
                return _deploymentRuns.Values.ToArray();
        }
    }

    public IReadOnlyCollection<ElsaInstanceLifecycleResolvedPlan> ResolvedPlans
    {
        get
        {
            lock (_gate)
                return _resolvedPlans.Values.ToArray();
        }
    }

    public IReadOnlyCollection<ElsaInstanceLifecycleRecordedFailure> Failures
    {
        get
        {
            lock (_gate)
                return _failures.Values.ToArray();
        }
    }

    /// <summary>
    /// Records the uncertain post-submission state used by local compositions and
    /// deterministic core tests. Durable adapters perform the equivalent transition
    /// while retaining their existing deployment-run reservation.
    /// </summary>
    public void MarkRecoveryRequired(Guid operationId)
    {
        lock (_gate)
        {
            if (!_operations.TryGetValue(operationId, out var operation) ||
                !_instances.TryGetValue(operation.InstanceId, out var instance))
                throw new KeyNotFoundException("Lifecycle operation does not exist.");
            if (operation.State == ElsaInstanceOperationState.Accepted)
                operation = operation.TransitionTo(ElsaInstanceOperationState.Queued);
            if (operation.State == ElsaInstanceOperationState.Queued)
                operation = operation.TransitionTo(ElsaInstanceOperationState.Running);
            if (operation.State == ElsaInstanceOperationState.Running)
                operation = operation.TransitionTo(ElsaInstanceOperationState.RecoveryRequired);
            if (operation.State != ElsaInstanceOperationState.RecoveryRequired)
                throw new ElsaInstanceLifecycleConflictException("Lifecycle operation cannot require provider recovery.");
            _operations[operationId] = operation;
            _instances[instance.Id] = ElsaInstance.Hydrate(
                instance.Id, instance.OrganizationId, instance.WorkspaceId, instance.Name, instance.Slug, instance.Intent,
                ElsaObservedLifecycle.Unknown, ElsaInstanceHealth.Unknown, instance.Version, instance.IdentityBinding,
                instance.DesiredStateRevisionId, instance.ResolvedPlanReference, instance.CurrentResolvedRelease,
                instance.CurrentDeploymentReference, instance.PlacementAssignmentReference, instance.ElsaTenantReference,
                instance.LastOperationId);
        }
    }

    public Task<ElsaInstanceProviderReconciliationTarget?> GetTargetAsync(
        Guid workspaceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_operations.TryGetValue(operationId, out var operation) ||
                !_instances.TryGetValue(operation.InstanceId, out var instance) ||
                instance.WorkspaceId != workspaceId || operation.State != ElsaInstanceOperationState.RecoveryRequired)
                return Task.FromResult<ElsaInstanceProviderReconciliationTarget?>(null);
            return Task.FromResult<ElsaInstanceProviderReconciliationTarget?>(new(
                instance,
                operation,
                _reconciliationResults.GetValueOrDefault(operationId)?.Version ?? 0));
        }
    }

    public Task<ElsaInstanceProviderReconciliationResult?> GetResultAsync(
        Guid workspaceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var stored = _reconciliationResults.GetValueOrDefault(operationId);
            return Task.FromResult<ElsaInstanceProviderReconciliationResult?>(
                stored is not null && stored.Result.Projection.WorkspaceId == workspaceId &&
                stored.Result.Projection.OperationState != ElsaInstanceOperationState.RecoveryRequired ? stored.Result : null);
        }
    }

    public Task<ElsaInstanceProviderReconciliationResult> CommitAsync(
        ElsaInstanceProviderReconciliationCommit commit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(commit);
        commit.Validate();
        lock (_gate)
        {
            if (_reconciliationResults.TryGetValue(commit.OperationId, out var replay))
            {
                if (!string.Equals(replay.EvidenceFingerprint, commit.EvidenceFingerprint, StringComparison.Ordinal))
                {
                    if (replay.Result.Projection.OperationState != ElsaInstanceOperationState.RecoveryRequired ||
                        replay.Version != commit.ExpectedReconciliationVersion)
                        throw new ElsaInstanceLifecycleConflictException("Provider reconciliation evidence conflicts with the recorded result.");
                }
                else
                {
                    return Task.FromResult(replay.Result with { Replayed = true });
                }
            }
            else if (commit.ExpectedReconciliationVersion != 0)
                throw new ElsaInstanceLifecycleConflictException("Provider reconciliation target changed concurrently.");
            if (!_operations.TryGetValue(commit.OperationId, out var operation) ||
                !_instances.TryGetValue(commit.InstanceId, out var instance) ||
                instance.WorkspaceId != commit.WorkspaceId || operation.InstanceId != commit.InstanceId ||
                operation.State != ElsaInstanceOperationState.RecoveryRequired ||
                operation.AttemptNumber != commit.ExpectedAttemptNumber || instance.Version != commit.ExpectedInstanceVersion)
                throw new ElsaInstanceLifecycleConflictException("Provider reconciliation target changed concurrently.");

            var persistedInstance = WithVersion(commit.Instance, checked(commit.ExpectedInstanceVersion + 1));
            _instances[commit.InstanceId] = persistedInstance;
            _operations[commit.OperationId] = commit.Operation;
            var outcome = commit.Operation.State switch
            {
                ElsaInstanceOperationState.Succeeded => ElsaInstanceProviderReconciliationOutcome.Converged,
                ElsaInstanceOperationState.Failed when commit.DiagnosticCode == ElsaInstanceProviderReconciliationService.HealthFailedCode ||
                    commit.DiagnosticCode == ElsaInstanceProviderReconciliationService.HealthUnknownCode => ElsaInstanceProviderReconciliationOutcome.HealthGateFailed,
                ElsaInstanceOperationState.Failed => ElsaInstanceProviderReconciliationOutcome.Failed,
                _ => ElsaInstanceProviderReconciliationOutcome.RecoveryRequired
            };
            var result = new ElsaInstanceProviderReconciliationResult(
                outcome, Projection(commit, persistedInstance.Version), commit.DiagnosticCode, commit.RetrySafe, false, commit.ReconciledAt);
            _reconciliationResults[commit.OperationId] = new(
                commit.EvidenceFingerprint,
                checked(commit.ExpectedReconciliationVersion + 1),
                result);
            return Task.FromResult(result);
        }
    }

    private static ElsaInstanceProviderReconciliationProjection Projection(
        ElsaInstanceProviderReconciliationCommit commit,
        int instanceVersion) => new(
        commit.WorkspaceId,
        commit.InstanceId,
        commit.OperationId,
        commit.Operation.AttemptNumber,
        commit.Instance.ObservedLifecycle,
        commit.Instance.Health,
        instanceVersion,
        commit.Operation.State);

    private static ElsaInstance WithVersion(ElsaInstance instance, int version) => ElsaInstance.Hydrate(
        instance.Id,
        instance.OrganizationId,
        instance.WorkspaceId,
        instance.Name,
        instance.Slug,
        instance.Intent,
        instance.ObservedLifecycle,
        instance.Health,
        version,
        instance.IdentityBinding,
        instance.DesiredStateRevisionId,
        instance.ResolvedPlanReference,
        instance.CurrentResolvedRelease,
        instance.CurrentDeploymentReference,
        instance.PlacementAssignmentReference,
        instance.ElsaTenantReference,
        instance.LastOperationId,
        instance.DeletedAt);

    /// <summary>
    /// Supplies safe, already-admitted resolution inputs to the worker seam. A real
    /// adapter reconstructs these from governed persistence after reading the ID-only
    /// outbox; this recording store keeps them explicit for core tests.
    /// </summary>
    public void RegisterResolutionInput(Guid operationId, ElsaInstanceLifecycleResolutionInput input)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID is required.", nameof(operationId));
        ArgumentNullException.ThrowIfNull(input);
        lock (_gate)
        {
            if (!_operations.ContainsKey(operationId))
                throw new KeyNotFoundException("Lifecycle operation does not exist.");
            _resolutionInputs[operationId] = input;
        }
    }

    public Task<ElsaInstance?> GetInstanceAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<ElsaInstance?>(_instances.TryGetValue(instanceId, out var instance) && instance.WorkspaceId == workspaceId
                ? instance
                : null);
        }
    }

    public Task<ElsaInstanceOperation?> GetActiveOperationAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var operation = _operations.Values
                .Where(x => x.InstanceId == instanceId && ElsaInstanceOperationGuard.IsBlocking(x.State))
                .OrderByDescending(x => ElsaInstanceOperationGuard.IsActive(x.State))
                .ThenByDescending(x => x.AcceptedAt)
                .FirstOrDefault();
            return Task.FromResult<ElsaInstanceOperation?>(operation is not null &&
                _instances.TryGetValue(instanceId, out var instance) && instance.WorkspaceId == workspaceId
                ? operation
                : null);
        }
    }

    public Task<ElsaInstanceOperation?> FindOperationByKeyAsync(
        Guid workspaceId,
        string idempotencyKey,
        Guid? instanceId = null,
        ElsaInstanceOperationAction? action = null,
        string? idempotencyScope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        idempotencyKey = RequireKey(idempotencyKey);
        lock (_gate)
        {
            var operation = _operations.Values
                .Where(x => x.IdempotencyKey == idempotencyKey)
                .Where(x => instanceId is null || x.InstanceId == instanceId)
                .Where(x => action is null || x.Action == action)
                .Where(x => idempotencyScope is null || x.IdempotencyScope == idempotencyScope)
                .Where(x => _instances.TryGetValue(x.InstanceId, out var instance) && instance.WorkspaceId == workspaceId)
                .OrderByDescending(x => x.AcceptedAt)
                .FirstOrDefault();
            return Task.FromResult<ElsaInstanceOperation?>(operation);
        }
    }

    public Task<ElsaInstanceLifecycleAcceptance> CommitAcceptedAsync(
        ElsaInstance? expectedInstance,
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        ElsaInstanceLifecycleOutboxMessage outbox,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(outbox);

        lock (_gate)
        {
            if (operation.InstanceId != instance.Id || outbox.InstanceId != instance.Id || outbox.OperationId != operation.Id)
                throw new ElsaInstanceLifecycleConflictException("Lifecycle operation identity is inconsistent.");
            if (outbox.WorkspaceId != instance.WorkspaceId || operation.IdempotencyKey.Length == 0)
                throw new ElsaInstanceLifecycleConflictException("Lifecycle operation scope is invalid.");

            if (_operations.TryGetValue(operation.Id, out var storedOperation))
            {
                if (storedOperation.InstanceId != operation.InstanceId ||
                    storedOperation.Action != operation.Action ||
                    storedOperation.ExpectedVersion != operation.ExpectedVersion ||
                    !string.Equals(storedOperation.IdempotencyScope, operation.IdempotencyScope, StringComparison.Ordinal) ||
                    !string.Equals(storedOperation.IdempotencyKey, operation.IdempotencyKey, StringComparison.Ordinal) ||
                    !string.Equals(storedOperation.RequestHash, operation.RequestHash, StringComparison.Ordinal))
                    throw new ElsaInstanceLifecycleConflictException("Lifecycle operation identity is already in use.");

                if (storedOperation.State == operation.State && storedOperation.AttemptNumber == operation.AttemptNumber)
                    return Task.FromResult(Replay(instance, storedOperation));

                var isRecoveryResume = storedOperation.State == ElsaInstanceOperationState.RecoveryRequired &&
                    operation.State == ElsaInstanceOperationState.Queued &&
                    operation.AttemptNumber == storedOperation.AttemptNumber + 1;
                if (isRecoveryResume && storedOperation.Action != ElsaInstanceOperationAction.Delete &&
                    (!_reconciliationResults.TryGetValue(operation.Id, out var reconciliation) ||
                     !reconciliation.Result.RetrySafe))
                    throw new ElsaInstanceLifecycleConflictException(
                        "Provider reconciliation has not established that retry is safe.");
                if ((!ElsaInstanceOperation.CanTransition(storedOperation.State, operation.State) && !isRecoveryResume) ||
                    operation.AttemptNumber < storedOperation.AttemptNumber)
                    throw new ElsaInstanceLifecycleConflictException("Lifecycle operation state transition is not valid.");

                if (!_instances.TryGetValue(instance.Id, out var existingForUpdate) ||
                    existingForUpdate.WorkspaceId != instance.WorkspaceId)
                    throw new ElsaInstanceLifecycleConflictException("Elsa instance does not exist in the workspace.");
                if (expectedInstance is not null && existingForUpdate.Version != expectedInstance.Version)
                    throw new ElsaInstanceLifecycleConflictException("Instance version conflict.", ElsaInstanceLifecycleConflictReason.VersionConflict);

                _instances[instance.Id] = instance;
                _operations[operation.Id] = operation;
                _outbox[outbox.Id] = outbox;
                return Task.FromResult(new ElsaInstanceLifecycleAcceptance(instance, operation, outbox, false));
            }

            var sameIdentity = _operations.Values.FirstOrDefault(x =>
                x.IdempotencyScope == operation.IdempotencyScope &&
                x.IdempotencyKey == operation.IdempotencyKey);
            if (sameIdentity is not null)
            {
                if (sameIdentity.InstanceId != operation.InstanceId ||
                    !string.Equals(sameIdentity.RequestHash, operation.RequestHash, StringComparison.Ordinal))
                    throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.", ElsaInstanceLifecycleConflictReason.IdempotencyConflict);
                return Task.FromResult(Replay(instance, sameIdentity));
            }

            var instanceExists = _instances.TryGetValue(instance.Id, out var existing);
            if (expectedInstance is null)
            {
                if (instanceExists)
                    throw new ElsaInstanceLifecycleConflictException("Elsa instance identity is already in use.");
                if (operation.Action != ElsaInstanceOperationAction.Create)
                    throw new ElsaInstanceLifecycleConflictException("A lifecycle operation requires an existing instance.");
            }
            else
            {
                if (!instanceExists || existing!.WorkspaceId != instance.WorkspaceId)
                    throw new ElsaInstanceLifecycleConflictException("Elsa instance does not exist in the workspace.");
                if (existing.Version != expectedInstance.Version)
                    throw new ElsaInstanceLifecycleConflictException("Instance version conflict.", ElsaInstanceLifecycleConflictReason.VersionConflict);
            }

            var active = _operations.Values.FirstOrDefault(x =>
                x.InstanceId == instance.Id && ElsaInstanceOperationGuard.IsBlocking(x.State));
            if (active is not null)
            {
                var isDeleteSuccessor = operation.Action == ElsaInstanceOperationAction.Delete &&
                    operation.State == ElsaInstanceOperationState.WaitingForPriorOperation &&
                    active.Action != ElsaInstanceOperationAction.Delete;
                if (!isDeleteSuccessor)
                    throw new ElsaInstanceLifecycleConflictException("An instance operation is already active.", ElsaInstanceLifecycleConflictReason.OperationActive);
            }

            _instances[instance.Id] = instance;
            _operations[operation.Id] = operation;
            _outbox[outbox.Id] = outbox;
            return Task.FromResult(new ElsaInstanceLifecycleAcceptance(instance, operation, outbox, false));
        }
    }

    public Task<ElsaInstanceLifecycleAcceptance> CommitAcceptedWithContextAsync(
        ElsaInstance? expectedInstance,
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        ElsaInstanceLifecycleOutboxMessage outbox,
        ElsaInstanceAcceptanceContext context,
        CancellationToken cancellationToken = default) =>
        CommitAcceptedAsync(expectedInstance, instance, operation, outbox, cancellationToken);

    public Task<ElsaInstanceLifecycleWorkItem?> TryClaimNextAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Lifecycle worker identity is required.", nameof(workerId));

        lock (_gate)
        {
            foreach (var outbox in _outbox.Values.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
            {
                if (!_operations.TryGetValue(outbox.OperationId, out var operation) ||
                    !_instances.TryGetValue(outbox.InstanceId, out var instance))
                    continue;

                // Delete work has a distinct cleanup/evidence boundary. It must
                // never be consumed by the plan-resolution worker.
                if (operation.Action == ElsaInstanceOperationAction.Delete)
                    continue;

                // Waiting deletes are durable successors, not resolver work. They
                // become eligible once their prior operation reaches a terminal
                // state.
                if (operation.State == ElsaInstanceOperationState.WaitingForPriorOperation)
                {
                    var priorOperationIsActive = _operations.Values.Any(x =>
                        x.Id != operation.Id &&
                        x.InstanceId == operation.InstanceId &&
                        ElsaInstanceOperationGuard.IsBlocking(x.State));
                    if (priorOperationIsActive)
                        continue;

                    operation = operation.TransitionTo(ElsaInstanceOperationState.Accepted);
                    _operations[operation.Id] = operation;
                }
                if (operation.State != ElsaInstanceOperationState.Accepted)
                    continue;

                var nowUtc = now.ToUniversalTime();
                if (_claims.TryGetValue(operation.Id, out var existingClaim) && existingClaim.ExpiresAt > nowUtc)
                    continue;
                var claim = new LifecycleClaim(
                    workerId.Trim(),
                    CreateLeaseToken(),
                    existingClaim is null ? 1 : checked(existingClaim.Version + 1),
                    nowUtc.Add(WorkerLeaseDuration));
                _claims[operation.Id] = claim;
                var input = _resolutionInputs.GetValueOrDefault(operation.Id);
                return Task.FromResult<ElsaInstanceLifecycleWorkItem?>(
                    new ElsaInstanceLifecycleWorkItem(outbox, operation, instance, input!, claim.Token, claim.Version));
            }

            return Task.FromResult<ElsaInstanceLifecycleWorkItem?>(null);
        }
    }

    public Task<ElsaInstanceDeletionWorkItem?> TryClaimNextDeletionAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Deletion worker identity is required.", nameof(workerId));

        lock (_gate)
        {
            foreach (var outbox in _outbox.Values
                         .Where(x => x.Action == ElsaInstanceOperationAction.Delete)
                         .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
            {
                if (!_operations.TryGetValue(outbox.OperationId, out var operation) ||
                    !_instances.TryGetValue(outbox.InstanceId, out var instance))
                    continue;
                var uncertainRun = _deploymentRuns.Values.Any(x => x.InstanceId == instance.Id &&
                    x.Run.Status is WorkspaceDeploymentRunStatus.Queued or WorkspaceDeploymentRunStatus.Running or
                        WorkspaceDeploymentRunStatus.RecoveryRequired);
                if (uncertainRun)
                    continue;
                if (operation.State == ElsaInstanceOperationState.WaitingForPriorOperation)
                {
                    var priorBlocking = _operations.Values.Any(x => x.Id != operation.Id &&
                        x.InstanceId == instance.Id && ElsaInstanceOperationGuard.IsBlocking(x.State));
                    if (priorBlocking)
                        continue;
                    operation = operation.TransitionTo(ElsaInstanceOperationState.Accepted);
                    _operations[operation.Id] = operation;
                }
                if (operation.State is not (ElsaInstanceOperationState.Accepted or ElsaInstanceOperationState.Queued or ElsaInstanceOperationState.Running))
                    continue;

                var nowUtc = now.ToUniversalTime();
                if (_claims.TryGetValue(operation.Id, out var existingClaim) && existingClaim.ExpiresAt > nowUtc)
                    continue;
                var claim = new LifecycleClaim(workerId.Trim(), CreateLeaseToken(),
                    existingClaim is null ? 1 : checked(existingClaim.Version + 1), nowUtc.Add(WorkerLeaseDuration));
                if (operation.State == ElsaInstanceOperationState.Queued)
                {
                    operation = operation.TransitionTo(ElsaInstanceOperationState.Running);
                    _operations[operation.Id] = operation;
                }
                _claims[operation.Id] = claim;
                var correlatedRun = _deploymentRuns.Values
                    .Where(x => x.InstanceId == instance.Id)
                    .OrderByDescending(x => x.Run.CreatedAt)
                    .Select(x => (Guid?)x.Run.Id)
                    .FirstOrDefault();
                var local = instance.ObservedLifecycle != ElsaObservedLifecycle.Unknown &&
                    instance.CurrentDeploymentReference is null && instance.PlacementAssignmentReference is null &&
                    instance.ElsaTenantReference is null;
                return Task.FromResult<ElsaInstanceDeletionWorkItem?>(new(
                    outbox, operation, instance, local, correlatedRun, claim.Token, claim.Version));
            }
            return Task.FromResult<ElsaInstanceDeletionWorkItem?>(null);
        }
    }

    public Task<bool> RenewDeletionLeaseAsync(ElsaInstanceDeletionWorkItem item, string workerId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_claims.TryGetValue(item.Operation.Id, out var claim) ||
                !string.Equals(claim.WorkerId, workerId, StringComparison.Ordinal) ||
                !string.Equals(claim.Token, item.LeaseToken, StringComparison.Ordinal) ||
                claim.Version != item.LeaseVersion || claim.ExpiresAt <= now.ToUniversalTime())
                return Task.FromResult(false);

            _claims[item.Operation.Id] = claim with { ExpiresAt = now.ToUniversalTime().Add(WorkerLeaseDuration) };
            return Task.FromResult(true);
        }
    }

    public Task<ElsaInstanceDeletionResult> CommitDeletionAsync(
        ElsaInstanceDeletionCommit commit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(commit);
        commit.Validate();
        lock (_gate)
        {
            if (_deletionResults.TryGetValue(commit.OperationId, out var stored))
            {
                if (!string.Equals(stored.Fingerprint, commit.EvidenceFingerprint, StringComparison.Ordinal))
                    throw new ElsaInstanceLifecycleConflictException("Deletion evidence conflicts with the terminal result.");
                return Task.FromResult(stored.Result with { Replayed = true });
            }

            EnsureDeletionOwnership(commit.WorkspaceId, commit.InstanceId, commit.OperationId, commit.OutboxId,
                commit.ExpectedInstanceVersion, commit.ExpectedAttemptNumber, commit.WorkerId, commit.LeaseToken, commit.LeaseVersion);
            var currentInstance = _instances[commit.InstanceId];
            if (commit.ProofKind == ElsaInstanceDeletionProofKind.LocalNoOwnedResources &&
                (currentInstance.ObservedLifecycle == ElsaObservedLifecycle.Unknown ||
                 currentInstance.CurrentDeploymentReference is not null ||
                 currentInstance.PlacementAssignmentReference is not null ||
                 currentInstance.ElsaTenantReference is not null))
                throw new ElsaInstanceLifecycleConflictException("Local deletion proof is not valid for this instance.");
            if (commit.ExpectedRunId is { } runId && (!_deploymentRuns.TryGetValue(runId, out var run) ||
                run.InstanceId != commit.InstanceId || run.Run.Status is WorkspaceDeploymentRunStatus.Queued or WorkspaceDeploymentRunStatus.Running or WorkspaceDeploymentRunStatus.RecoveryRequired))
                throw new ElsaInstanceLifecycleConflictException("Deletion run correlation is not terminal.");

            var tombstone = WithVersion(commit.Instance, checked(commit.ExpectedInstanceVersion + 1));
            _instances[commit.InstanceId] = tombstone;
            _operations[commit.OperationId] = commit.Operation;
            _claims.Remove(commit.OperationId);
            var result = new ElsaInstanceDeletionResult(ElsaInstanceDeletionOutcome.Deleted, commit.Operation,
                tombstone, commit.DiagnosticCode, commit.EvidenceFingerprint, false);
            _deletionResults[commit.OperationId] = new(commit.EvidenceFingerprint, result);
            return Task.FromResult(result);
        }
    }

    public Task<ElsaInstanceDeletionResult> RequireDeletionRecoveryAsync(
        ElsaInstanceDeletionFailure failure,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(failure);
        failure.Validate();
        lock (_gate)
        {
            EnsureDeletionOwnership(failure.WorkspaceId, failure.InstanceId, failure.OperationId, failure.OutboxId,
                failure.ExpectedInstanceVersion, failure.ExpectedAttemptNumber, failure.WorkerId, failure.LeaseToken, failure.LeaseVersion);
            var operation = _operations[failure.OperationId];
            if (operation.State == ElsaInstanceOperationState.Accepted)
                operation = operation.TransitionTo(ElsaInstanceOperationState.Queued);
            operation = operation.TransitionTo(ElsaInstanceOperationState.RecoveryRequired);
            _operations[operation.Id] = operation;
            _claims.Remove(operation.Id);
            var instance = _instances[failure.InstanceId];
            var result = new ElsaInstanceDeletionResult(ElsaInstanceDeletionOutcome.RecoveryRequired, operation,
                instance, failure.DiagnosticCode, failure.EvidenceFingerprint, false);
            return Task.FromResult(result);
        }
    }

    private void EnsureDeletionOwnership(Guid workspaceId, Guid instanceId, Guid operationId, Guid outboxId,
        int expectedVersion, int expectedAttempt, string workerId, string leaseToken, int leaseVersion)
    {
        if (!_instances.TryGetValue(instanceId, out var instance) || instance.WorkspaceId != workspaceId || instance.Version != expectedVersion ||
            !_operations.TryGetValue(operationId, out var operation) || operation.Action != ElsaInstanceOperationAction.Delete ||
            operation.State is not (ElsaInstanceOperationState.Accepted or ElsaInstanceOperationState.Running) ||
            operation.AttemptNumber != expectedAttempt ||
            !_outbox.TryGetValue(outboxId, out var outbox) || outbox.OperationId != operationId ||
            !_claims.TryGetValue(operationId, out var claim) || !string.Equals(claim.WorkerId, workerId, StringComparison.Ordinal) ||
            !string.Equals(claim.Token, leaseToken, StringComparison.Ordinal) || claim.Version != leaseVersion ||
            claim.ExpiresAt <= _timeProvider.GetUtcNow())
            throw new ElsaInstanceLifecycleConflictException("Deletion work item is no longer owned by this worker.");
    }

    public Task<ElsaInstanceLifecycleWorkerResult> CommitResolvedAsync(
        ElsaInstanceLifecycleResolutionCommit commit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(commit);
        commit.Validate();

        lock (_gate)
        {
            if (!_operations.TryGetValue(commit.OperationId, out var currentOperation) ||
                !_instances.TryGetValue(commit.InstanceId, out var currentInstance) ||
                !_outbox.Values.Any(x => x.Id == commit.OutboxId && x.OperationId == commit.OperationId))
                throw new ElsaInstanceLifecycleConflictException("Lifecycle work item no longer exists.");

            var currentOutbox = _outbox.Values.Single(x => x.Id == commit.OutboxId);
            if (currentOutbox.WorkspaceId != commit.WorkspaceId || currentOutbox.InstanceId != commit.InstanceId ||
                currentOutbox.Action != currentOperation.Action ||
                !string.Equals(currentOutbox.RequestHash, commit.RequestHash, StringComparison.Ordinal))
                throw new ElsaInstanceLifecycleConflictException("Lifecycle work item envelope is inconsistent.");

            if (currentOperation.State == ElsaInstanceOperationState.Queued)
            {
                if (_deploymentRuns.Values.FirstOrDefault(x => x.Operation.Id == commit.OperationId) is not { } existingRun ||
                    existingRun.Run.WorkspaceId != commit.WorkspaceId ||
                    existingRun.Run.EnvironmentId != commit.DeploymentTarget.EnvironmentId ||
                    existingRun.Run.ApplicationId != commit.DeploymentTarget.ApplicationId ||
                    existingRun.InstanceId != commit.InstanceId ||
                    existingRun.Run.Status is not (WorkspaceDeploymentRunStatus.Queued or
                        WorkspaceDeploymentRunStatus.Running or
                        WorkspaceDeploymentRunStatus.RecoveryRequired))
                    throw new ElsaInstanceLifecycleConflictException("Lifecycle operation deployment run is inconsistent.");

                return Task.FromResult(new ElsaInstanceLifecycleWorkerResult(
                    ElsaInstanceLifecycleWorkerOutcome.AlreadyCompleted,
                    currentOperation,
                    currentInstance,
                    existingRun.Run));
            }

            if (currentOperation.State != ElsaInstanceOperationState.Accepted ||
                !_claims.TryGetValue(commit.OperationId, out var claim) ||
                !string.Equals(claim.WorkerId, commit.WorkerId, StringComparison.Ordinal) ||
                !string.Equals(claim.Token, commit.LeaseToken, StringComparison.Ordinal) ||
                claim.Version != commit.LeaseVersion ||
                claim.ExpiresAt <= _timeProvider.GetUtcNow() ||
                currentOperation.InstanceId != commit.InstanceId ||
                !string.Equals(currentOperation.RequestHash, commit.RequestHash, StringComparison.Ordinal))
                throw new ElsaInstanceLifecycleConflictException("Lifecycle work item is no longer owned by this worker.");

            var target = commit.DeploymentTarget;
            var activeRun = _deploymentRuns.Values.FirstOrDefault(x =>
                x.Run.WorkspaceId == commit.WorkspaceId &&
                x.Run.EnvironmentId == target.EnvironmentId &&
                x.Run.Status is WorkspaceDeploymentRunStatus.Queued or WorkspaceDeploymentRunStatus.Running or WorkspaceDeploymentRunStatus.RecoveryRequired);
            if (activeRun is not null)
            {
                var failed = currentOperation.TransitionTo(ElsaInstanceOperationState.Failed);
                _operations[commit.OperationId] = failed;
                _failures[commit.OperationId] = new ElsaInstanceLifecycleRecordedFailure(
                    commit.OperationId, "run.reservation.conflict", "Lifecycle target already has active work.", commit.CommittedAt);
                _claims.Remove(commit.OperationId);
                return Task.FromResult(new ElsaInstanceLifecycleWorkerResult(
                    ElsaInstanceLifecycleWorkerOutcome.Conflict, failed, currentInstance,
                    FailureCode: "run.reservation.conflict",
                    FailureSummary: "Lifecycle target already has active work."));
            }

            var planKey = PlanKey(commit.WorkspaceId, commit.InstanceId, commit.Plan.Reference.PlanId);
            if (_resolvedPlans.TryGetValue(planKey, out var existingPlan) &&
                (!Equals(existingPlan.Reference, commit.Plan.Reference) ||
                 !string.Equals(existingPlan.SerializedPlan, commit.Plan.SerializedPlan, StringComparison.Ordinal)))
                throw new ElsaInstanceLifecycleConflictException("Resolved plan identity is already bound to different content.");

            var runId = Guid.NewGuid();
            var run = new WorkspaceDeploymentRun(
                runId,
                commit.WorkspaceId,
                target.ApplicationId,
                target.EnvironmentId,
                target.EngineId,
                target.SourceRevisionId,
                PreviousDeployedRevisionId: null,
                RollbackSourceRunId: null,
                WorkspaceDeploymentRunStatus.Queued,
                DeploymentValidationOutcome.Passed,
                target.ConfirmationId,
                target.ActorAccountId,
                commit.CommittedAt,
                StartedAt: null,
                CompletedAt: null,
                commit.CommittedAt,
                WorkerId: null,
                WorkerHeartbeatAt: null,
                AttemptNumber: 1,
                RecoveryReason: null,
                FailureMessage: null);
            var storedPlan = commit.Plan;
            _resolvedPlans[planKey] = storedPlan;
            _instances[commit.InstanceId] = commit.Instance;
            _operations[commit.OperationId] = commit.Operation;
            _deploymentRuns[runId] = new ElsaInstanceLifecycleDeploymentRun(run, commit.Operation, commit.InstanceId);
            _claims.Remove(commit.OperationId);
            return Task.FromResult(new ElsaInstanceLifecycleWorkerResult(
                ElsaInstanceLifecycleWorkerOutcome.Queued,
                commit.Operation,
                commit.Instance,
                run));
        }
    }

    public Task<ElsaInstanceLifecycleWorkerResult> FailResolutionAsync(
        ElsaInstanceLifecycleResolutionFailure failure,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(failure);
        failure.Validate();

        lock (_gate)
        {
            if (!_operations.TryGetValue(failure.OperationId, out var currentOperation) ||
                !_instances.TryGetValue(failure.InstanceId, out var instance) ||
                !_outbox.Values.Any(x => x.Id == failure.OutboxId && x.OperationId == failure.OperationId))
                throw new ElsaInstanceLifecycleConflictException("Lifecycle work item no longer exists.");

            var currentOutbox = _outbox.Values.Single(x => x.Id == failure.OutboxId);
            if (currentOutbox.WorkspaceId != failure.WorkspaceId || currentOutbox.InstanceId != failure.InstanceId ||
                currentOutbox.Action != currentOperation.Action ||
                !string.Equals(currentOutbox.RequestHash, failure.RequestHash, StringComparison.Ordinal) ||
                instance.WorkspaceId != failure.WorkspaceId || currentOperation.InstanceId != failure.InstanceId)
                throw new ElsaInstanceLifecycleConflictException("Lifecycle work item envelope is inconsistent.");

            if (currentOperation.State == ElsaInstanceOperationState.Failed &&
                _failures.TryGetValue(failure.OperationId, out var existingFailure))
                return Task.FromResult(new ElsaInstanceLifecycleWorkerResult(
                    ElsaInstanceLifecycleWorkerOutcome.AlreadyCompleted,
                    currentOperation,
                    instance,
                    FailureCode: existingFailure.Code,
                    FailureSummary: existingFailure.Summary));
            if (currentOperation.State != ElsaInstanceOperationState.Accepted ||
                !_claims.TryGetValue(failure.OperationId, out var claim) ||
                !string.Equals(claim.WorkerId, failure.WorkerId, StringComparison.Ordinal) ||
                !string.Equals(claim.Token, failure.LeaseToken, StringComparison.Ordinal) ||
                claim.Version != failure.LeaseVersion ||
                claim.ExpiresAt <= _timeProvider.GetUtcNow() ||
                !string.Equals(currentOperation.RequestHash, failure.RequestHash, StringComparison.Ordinal))
                throw new ElsaInstanceLifecycleConflictException("Lifecycle work item is no longer owned by this worker.");

            var failed = currentOperation.TransitionTo(ElsaInstanceOperationState.Failed);
            _operations[failure.OperationId] = failed;
            _claims.Remove(failure.OperationId);
            _failures[failure.OperationId] = new ElsaInstanceLifecycleRecordedFailure(
                failure.OperationId, failure.Code, failure.Summary, failure.FailedAt);
            return Task.FromResult(new ElsaInstanceLifecycleWorkerResult(
                ElsaInstanceLifecycleWorkerOutcome.Failed,
                failed,
                instance,
                FailureCode: failure.Code,
                FailureSummary: failure.Summary));
        }
    }

    private ElsaInstanceLifecycleAcceptance Replay(ElsaInstance requestedInstance, ElsaInstanceOperation operation)
    {
        var instance = _instances.TryGetValue(operation.InstanceId, out var storedInstance)
            ? storedInstance
            : requestedInstance;
        if (_outbox.Values.Where(x => x.OperationId == operation.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault() is not { } outbox)
            throw new ElsaInstanceLifecycleConflictException("Lifecycle operation outbox record is missing.");
        return new ElsaInstanceLifecycleAcceptance(instance, operation, outbox, true);
    }

    private static string PlanKey(Guid workspaceId, Guid instanceId, string planId) =>
        $"{workspaceId:N}:{instanceId:N}:{planId}";

    private static string RequireKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key is required.", nameof(value));
        return value.Trim();
    }

    private static string CreateLeaseToken() =>
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private sealed record LifecycleClaim(string WorkerId, string Token, int Version, DateTimeOffset ExpiresAt);

    private sealed record StoredReconciliationResult(
        string EvidenceFingerprint,
        int Version,
        ElsaInstanceProviderReconciliationResult Result);

    private sealed record StoredDeletionResult(string Fingerprint, ElsaInstanceDeletionResult Result);
}
