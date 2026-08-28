using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Plans;
using ElsaControl.Deployment.Abstractions.Targets;
using static ElsaControl.Deployment.Abstractions.DeploymentGuard;

namespace ElsaControl.Deployment.Abstractions;

/// <summary>
/// Outcome of validation, diff, dry-run, or apply execution.
/// </summary>
public sealed record DeploymentResult
{
    public DeploymentResult(
        string deploymentId,
        DeploymentOperationMode mode,
        DeploymentStatus status,
        DeploymentArtifactIdentity artifact,
        DeploymentTargetDescriptor target,
        DeploymentPlan? plan = null,
        IEnumerable<DeploymentResourceResult>? resourceResults = null,
        IEnumerable<DeploymentDiagnostic>? diagnostics = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null)
    {
        DeploymentId = Require(deploymentId, nameof(deploymentId));
        Mode = mode;
        Status = status;
        Artifact = artifact;
        Target = target;
        Plan = plan;
        ResourceResults = (resourceResults ?? []).ToArray();
        Diagnostics = (diagnostics ?? []).ToArray();
        StartedAt = (startedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        CompletedAt = completedAt?.ToUniversalTime();
    }

    public string DeploymentId { get; }

    public DeploymentOperationMode Mode { get; }

    public DeploymentStatus Status { get; }

    public DeploymentArtifactIdentity Artifact { get; }

    public DeploymentTargetDescriptor Target { get; }

    public DeploymentPlan? Plan { get; }

    public IReadOnlyCollection<DeploymentResourceResult> ResourceResults { get; }

    public IReadOnlyCollection<DeploymentDiagnostic> Diagnostics { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }
}
