using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Targets;
using static Elsa.Platform.Deployment.Abstractions.DeploymentGuard;

namespace Elsa.Platform.Deployment.Abstractions.Plans;

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
