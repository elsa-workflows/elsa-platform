namespace ElsaControl.Deployment.Azure;

/// <summary>
/// The atomic outcome of an operation create-or-get reservation. <see cref="Replayed"/> is
/// assigned by the durable store while selecting or inserting the reservation; it must not be
/// inferred from executor attempts, which can also increase after retries or restarts.
/// </summary>
public sealed record AzureProviderOperationCreateResult(
    AzureProviderOperation Operation,
    bool Replayed);

public interface IAzureProviderOperationStore
{
    Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates or gets an operation and reports whether the durable reservation was
    /// already present. The replay bit is part of the persistence contract and must be assigned
    /// by the implementation that owns the create-or-get race.
    /// </summary>
    Task<AzureProviderOperationCreateResult> CreateOrGetWithResultAsync(
        AzureProviderOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureProviderOperation>> ListRunnableAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, string? providerScopeFingerprint, CancellationToken cancellationToken = default);
    /// <summary>
    /// Transitions an operation whose persisted provider plan cannot be restored to a value-free
    /// failure. Recovery-required operations retain their recovery status and target reservation
    /// for explicit operator reconciliation. The compare-and-set version prevents a stale worker
    /// from changing a concurrently claimed or completed operation.
    /// </summary>
    Task<AzureProviderOperation?> MarkUnrestorableAsync(
        Guid workspaceId,
        Guid operationId,
        DateTimeOffset now,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default);
}
