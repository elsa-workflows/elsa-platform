using ValenceControl.Deployment.Core.Workspace;

namespace ValenceControl.Deployment.Core.Cockpit;

public sealed class DeploymentCockpitService(IWorkspaceDeploymentStore store)
{
    public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.GetCockpitAsync(workspaceId, cancellationToken);
}
