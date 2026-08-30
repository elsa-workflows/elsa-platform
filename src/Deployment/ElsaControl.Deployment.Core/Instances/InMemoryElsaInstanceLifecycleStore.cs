using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Deterministic recording store for core tests and local composition. Its lock
/// models the atomic boundary required of the relational implementation without
/// introducing persistence or provider concerns into the lifecycle service.
/// </summary>
public sealed class InMemoryElsaInstanceLifecycleStore : IElsaInstanceLifecycleStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ElsaInstance> _instances = [];
    private readonly Dictionary<Guid, ElsaInstanceOperation> _operations = [];
    private readonly Dictionary<Guid, ElsaInstanceLifecycleOutboxMessage> _outbox = [];

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
                .OrderByDescending(x => x.AcceptedAt)
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

    private ElsaInstanceLifecycleAcceptance Replay(ElsaInstance requestedInstance, ElsaInstanceOperation operation)
    {
        var instance = _instances.TryGetValue(operation.InstanceId, out var storedInstance)
            ? storedInstance
            : requestedInstance;
        if (_outbox.Values.Where(x => x.OperationId == operation.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault() is not { } outbox)
            throw new ElsaInstanceLifecycleConflictException("Lifecycle operation outbox record is missing.");
        return new ElsaInstanceLifecycleAcceptance(instance, operation, outbox, true);
    }

    private static string RequireKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key is required.", nameof(value));
        return value.Trim();
    }
}
