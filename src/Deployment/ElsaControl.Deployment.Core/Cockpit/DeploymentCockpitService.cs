using ElsaControl.Deployment.Core.Workspace;

namespace ElsaControl.Deployment.Core.Cockpit;

public sealed class DeploymentCockpitService(IWorkspaceDeploymentStore store)
{
    public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.GetCockpitAsync(workspaceId, cancellationToken);
}
