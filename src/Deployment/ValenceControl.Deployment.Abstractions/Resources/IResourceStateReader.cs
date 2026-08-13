using ValenceControl.Deployment.Abstractions.Targets;

namespace ValenceControl.Deployment.Abstractions.Resources;

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
