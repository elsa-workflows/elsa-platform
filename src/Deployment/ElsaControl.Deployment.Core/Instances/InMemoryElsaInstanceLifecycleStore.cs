using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Deterministic recording store for core tests and local composition. Its lock
/// models the atomic boundary required of the relational implementation without
/// introducing persistence or provider concerns into the lifecycle service.
/// </summary>
public sealed class InMemoryElsaInstanceLifecycleStore(TimeProvider? timeProvider = null) : IElsaInstanceLifecycleStore, IElsaInstanceLifecycleWorkerStore
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
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        idempotencyKey = RequireKey(idempotencyKey);
        lock (_gate)
        {
            var operation = _operations.Values
                .Where(x => x.IdempotencyKey == idempotencyKey)
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
                if ((!ElsaInstanceOperation.CanTransition(storedOperation.State, operation.State) && !isRecoveryResume) ||
                    operation.AttemptNumber < storedOperation.AttemptNumber)
                    throw new ElsaInstanceLifecycleConflictException("Lifecycle operation state transition is not valid.");

                if (!_instances.TryGetValue(instance.Id, out var existingForUpdate) ||
                    existingForUpdate.WorkspaceId != instance.WorkspaceId)
                    throw new ElsaInstanceLifecycleConflictException("Elsa instance does not exist in the workspace.");
                if (expectedInstance is not null && existingForUpdate.Version != expectedInstance.Version)
                    throw new ElsaInstanceLifecycleConflictException("Instance version conflict.");

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
                    throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.");
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
                    throw new ElsaInstanceLifecycleConflictException("Instance version conflict.");
            }

            var active = _operations.Values.FirstOrDefault(x =>
                x.InstanceId == instance.Id && ElsaInstanceOperationGuard.IsBlocking(x.State));
            if (active is not null)
            {
                var isDeleteSuccessor = operation.Action == ElsaInstanceOperationAction.Delete &&
                    operation.State == ElsaInstanceOperationState.WaitingForPriorOperation &&
                    active.Action != ElsaInstanceOperationAction.Delete;
                if (!isDeleteSuccessor)
                    throw new ElsaInstanceLifecycleConflictException("An instance operation is already active.");
            }

            _instances[instance.Id] = instance;
            _operations[operation.Id] = operation;
            _outbox[outbox.Id] = outbox;
            return Task.FromResult(new ElsaInstanceLifecycleAcceptance(instance, operation, outbox, false));
        }
    }

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
}
