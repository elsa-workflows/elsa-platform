namespace ElsaControl.Deployment.Azure;

public interface IAzureProviderOperationStore
{
    Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureProviderOperation>> ListRunnableAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AzureProviderOperation>>([]);
    Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, CancellationToken cancellationToken = default);
    /// <summary>
    /// Transitions an operation whose persisted provider plan cannot be restored to a terminal,
    /// value-free failure. The compare-and-set version prevents a stale worker from changing a
    /// concurrently claimed or completed operation.
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
    Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, string message, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default);
}
