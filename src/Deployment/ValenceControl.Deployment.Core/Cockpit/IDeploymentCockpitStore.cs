namespace ValenceControl.Deployment.Core.Cockpit;

public interface IDeploymentCockpitStore
{
    Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
