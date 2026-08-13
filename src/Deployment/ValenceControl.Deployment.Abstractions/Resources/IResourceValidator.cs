using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Targets;

namespace ValenceControl.Deployment.Abstractions.Resources;

/// <summary>
/// Validates desired deployable resource state against a target.
/// </summary>
public interface IResourceValidator
{
    ValueTask<IReadOnlyCollection<DeploymentDiagnostic>> ValidateAsync(
        DeploymentResource resource,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);
}
