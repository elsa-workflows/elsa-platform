using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Plans;
using ValenceControl.Deployment.Abstractions.Targets;

namespace ValenceControl.Deployment.Abstractions;

/// <summary>
/// Host-agnostic entry point for deployment validation, diff, dry-run, and apply operations.
/// </summary>
public interface IDeploymentEngine
{
    ValueTask<DeploymentResult> ValidateAsync(
        IArtifactReader artifact,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentPlan> DiffAsync(
        IArtifactReader artifact,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentResult> DryRunAsync(
        DeploymentPlan plan,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentResult> ApplyAsync(
        DeploymentPlan plan,
        IDeploymentTarget target,
        DeploymentExecutionContext? context = null,
        CancellationToken cancellationToken = default);
}
