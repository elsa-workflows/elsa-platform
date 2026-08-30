using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Persistence port for the acceptance transaction. Implementations must atomically
/// commit the aggregate projection, operation and outbox message and must re-check
/// the expected version/unique reservations inside that transaction.
/// </summary>
public interface IElsaInstanceLifecycleStore
{
    Task<ElsaInstance?> GetInstanceAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default);

    Task<ElsaInstanceOperation?> GetActiveOperationAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds any operation for a workspace/key, including completed operations. This
    /// is needed to replay a create whose service-generated instance ID is unknown to
    /// the retried caller.
    /// </summary>
    Task<ElsaInstanceOperation?> FindOperationByKeyAsync(
        Guid workspaceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically persists an accepted mutation. A store must return the original
    /// operation/outbox for an exact replay and reject mismatched key/hash, version,
    /// or active-operation races with <see cref="ElsaInstanceLifecycleConflictException"/>.
    /// </summary>
    Task<ElsaInstanceLifecycleAcceptance> CommitAcceptedAsync(
        ElsaInstance? expectedInstance,
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        ElsaInstanceLifecycleOutboxMessage outbox,
        CancellationToken cancellationToken = default);
}
