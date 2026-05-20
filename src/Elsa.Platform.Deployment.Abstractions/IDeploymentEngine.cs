using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Targets;

namespace Elsa.Platform.Deployment.Abstractions;

/// <summary>
/// Host-agnostic entry point for deployment validation, diff, dry-run, and apply operations.
/// </summary>
public interface IDeploymentEngine
{
    ValueTask<DeploymentResult> ValidateAsync(
        IArtifactReader artifact,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentPlan> DiffAsync(
        IArtifactReader artifact,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentResult> DryRunAsync(
        DeploymentPlan plan,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentResult> ApplyAsync(
        DeploymentPlan plan,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);
}
