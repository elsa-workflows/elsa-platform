namespace ElsaControl.Deployment.Azure;

public interface IAzureProviderOperationStore
{
    Task<AzureProviderOperation> CreateOrGetAsync(AzureProviderOperationRequest request, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> GetLatestReconcileAsync(Guid workspaceId, string targetKey, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> ClaimAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> ClaimRecoveryAsync(Guid workspaceId, Guid operationId, string workerId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> HeartbeatAsync(Guid workspaceId, Guid operationId, string leaseToken, TimeSpan leaseDuration, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> CheckpointAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderCheckpoint checkpoint, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<AzureProviderOperation?> FinalizeAsync(Guid workspaceId, Guid operationId, string leaseToken, AzureProviderOperationStatus status, string code, DateTimeOffset now, long? expectedVersion = null, CancellationToken cancellationToken = default);
    Task<int> RecoverStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureProviderOperationTransition>> ListTransitionsAsync(Guid workspaceId, Guid operationId, CancellationToken cancellationToken = default);
}
