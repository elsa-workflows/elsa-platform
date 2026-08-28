using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Abstractions.Resources;

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
