using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Deployment.Abstractions.Resources;

/// <summary>
/// Current target-side state for a deployable resource.
/// </summary>
public sealed record DeploymentResourceState
{
    public DeploymentResourceState(
        DeploymentResourceId id,
        ArtifactDigest? stateHash = null,
        string? version = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Id = id;
        StateHash = stateHash;
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        Metadata = metadata ?? EmptyMetadata;
    }

    public DeploymentResourceId Id { get; }

    public ArtifactDigest? StateHash { get; }

    public string? Version { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>();
}
