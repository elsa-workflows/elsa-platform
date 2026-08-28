using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Plans;
using ElsaControl.Deployment.Abstractions.Targets;
using static ElsaControl.Deployment.Abstractions.DeploymentGuard;

namespace ElsaControl.Deployment.Abstractions.History;

/// <summary>
/// Append-oriented audit record for a deployment attempt.
/// </summary>
public sealed record DeploymentHistoryRecord
{
    public DeploymentHistoryRecord(
        string deploymentId,
        DeploymentArtifactIdentity artifact,
        DeploymentTargetDescriptor target,
        DeploymentStatus status,
        DeploymentActor? actor = null,
        DeploymentPlan? plan = null,
        IEnumerable<DeploymentResourceResult>? resourceResults = null,
        IEnumerable<DeploymentDiagnostic>? diagnostics = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null)
    {
        DeploymentId = Require(deploymentId, nameof(deploymentId));
        Artifact = artifact;
        ManifestDigest = artifact.ManifestDigest;
        Target = target;
        Status = status;
        Actor = actor;
        Plan = plan;
        ResourceResults = (resourceResults ?? []).ToArray();
        Diagnostics = (diagnostics ?? []).ToArray();
        StartedAt = (startedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        CompletedAt = completedAt?.ToUniversalTime();
    }

    public string DeploymentId { get; }

    public DeploymentArtifactIdentity Artifact { get; }

    public ArtifactDigest ManifestDigest { get; }

    public DeploymentTargetDescriptor Target { get; }

    public DeploymentActor? Actor { get; }

    public DeploymentStatus Status { get; }

    public DeploymentPlan? Plan { get; }

    public IReadOnlyCollection<DeploymentResourceResult> ResourceResults { get; }

    public IReadOnlyCollection<DeploymentDiagnostic> Diagnostics { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }
}
