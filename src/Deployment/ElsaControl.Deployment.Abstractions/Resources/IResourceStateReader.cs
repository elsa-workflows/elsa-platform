using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Abstractions.Resources;

/// <summary>
/// Reads current target state for a deployable resource.
/// </summary>
public interface IResourceStateReader
{
    ValueTask<DeploymentResourceState?> ReadAsync(
        DeploymentResourceId resourceId,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);
}
