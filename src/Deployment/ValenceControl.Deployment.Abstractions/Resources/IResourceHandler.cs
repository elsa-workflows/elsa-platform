using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Plans;
using ValenceControl.Deployment.Abstractions.Targets;

namespace ValenceControl.Deployment.Abstractions.Resources;

/// <summary>
/// Handles resource-specific read, validate, diff, dry-run, and apply behavior.
/// </summary>
public interface IResourceHandler
{
    string ResourceType { get; }

    ValueTask<DeploymentResourceState?> ReadAsync(
        DeploymentResource resource,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<DeploymentDiagnostic>> ValidateAsync(
        DeploymentResource resource,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentChange> DiffAsync(
        DeploymentResource desired,
        DeploymentResourceState? current,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentResourceResult> DryRunAsync(
        DeploymentChange change,
        DeploymentResource desired,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentResourceResult> ApplyAsync(
        DeploymentChange change,
        DeploymentResource desired,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default);
}
