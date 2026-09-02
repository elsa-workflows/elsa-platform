namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Tenant-scoped read port for the safe managed lifecycle operational snapshot.
/// Implementations must establish ownership from both supplied identifiers and
/// return no projection when either identifier does not identify the instance in
/// that workspace.
/// </summary>
public interface IManagedElsaInstanceOperationalStore
{
    Task<ManagedLifecycleOperationalHealthSnapshot?> GetSnapshotAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default);
}
