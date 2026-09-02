namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Provider-reconciliation orchestration boundary. Keeping the hosted worker
/// dependent on this contract lets it isolate submission/reconciliation failures
/// without coupling its scheduling loop to persistence details.
/// </summary>
public interface IElsaInstanceProviderReconciliationService
{
    Task<ElsaInstanceProviderReconciliationResult> ReconcileAsync(
        Guid workspaceId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}
