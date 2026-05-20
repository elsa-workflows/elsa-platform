using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Targets;

namespace Elsa.Platform.Deployment.Abstractions.Resources;

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
