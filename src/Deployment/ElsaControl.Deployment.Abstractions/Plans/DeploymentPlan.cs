using ElsaControl.Deployment.Abstractions.Artifacts;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.Targets;
using static ElsaControl.Deployment.Abstractions.DeploymentGuard;

namespace ElsaControl.Deployment.Abstractions.Plans;

/// <summary>
/// Deterministic deployment plan for one artifact and one target.
/// </summary>
public sealed record DeploymentPlan
{
    public DeploymentPlan(
        string id,
        DeploymentArtifactIdentity artifact,
        DeploymentTargetDescriptor target,
        IEnumerable<DeploymentChange> changes,
        IEnumerable<DeploymentDiagnostic>? diagnostics = null,
        DateTimeOffset? createdAt = null)
    {
        Id = Require(id, nameof(id));
        Artifact = artifact;
        Target = target;
        Changes = changes?.ToArray() ?? throw new ArgumentNullException(nameof(changes));
        Diagnostics = (diagnostics ?? []).ToArray();
        CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public string Id { get; }

    public DeploymentArtifactIdentity Artifact { get; }

    public DeploymentTargetDescriptor Target { get; }

    public IReadOnlyCollection<DeploymentChange> Changes { get; }

    public IReadOnlyCollection<DeploymentDiagnostic> Diagnostics { get; }

    public DateTimeOffset CreatedAt { get; }
}
