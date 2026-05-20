using static Elsa.Platform.Deployment.Abstractions.DeploymentGuard;

namespace Elsa.Platform.Deployment.Abstractions.Artifacts;

/// <summary>
/// Immutable identity for a deployment artifact.
/// </summary>
public sealed record DeploymentArtifactIdentity
{
    public DeploymentArtifactIdentity(
        string id,
        string schemaVersion,
        ArtifactDigest manifestDigest,
        ArtifactDigest contentDigest,
        string? version = null)
    {
        Id = Require(id, nameof(id));
        SchemaVersion = Require(schemaVersion, nameof(schemaVersion));
        ManifestDigest = manifestDigest;
        ContentDigest = contentDigest;
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    public string Id { get; }

    public string? Version { get; }

    public string SchemaVersion { get; }

    public ArtifactDigest ManifestDigest { get; }

    public ArtifactDigest ContentDigest { get; }
}
