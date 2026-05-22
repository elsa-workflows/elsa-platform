namespace Elsa.Platform.Deployment.Core.Cockpit;

public sealed class DeploymentCockpitService(IDeploymentCockpitStore store)
{
    public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.GetCockpitAsync(workspaceId, cancellationToken);
}
