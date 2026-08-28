using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Abstractions.History;

/// <summary>
/// Stores append-oriented deployment history records.
/// </summary>
public interface IDeploymentHistoryStore
{
    ValueTask RecordAsync(DeploymentHistoryRecord record, CancellationToken cancellationToken = default);

    ValueTask<DeploymentHistoryRecord?> FindAsync(string deploymentId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<DeploymentHistoryRecord> ListAsync(
        DeploymentTargetDescriptor target,
        CancellationToken cancellationToken = default);
}
