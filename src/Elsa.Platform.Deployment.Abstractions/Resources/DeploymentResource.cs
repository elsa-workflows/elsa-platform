using Elsa.Platform.Deployment.Abstractions;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Deployment.Abstractions.Resources;

/// <summary>
/// Desired deployable control-plane resource state.
/// </summary>
public sealed record DeploymentResource
{
    public DeploymentResource(
        DeploymentResourceId id,
        string? version = null,
        ArtifactDigest? desiredStateHash = null,
        IEnumerable<DeploymentResourceId>? dependencies = null,
        DeploymentDeletionBehavior deletion = DeploymentDeletionBehavior.Retain,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Id = id;
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        DesiredStateHash = desiredStateHash;
        Dependencies = (dependencies ?? []).ToArray();
        Deletion = deletion;
        Metadata = metadata ?? EmptyMetadata;
    }

    public DeploymentResourceId Id { get; }

    public string? Version { get; }

    public ArtifactDigest? DesiredStateHash { get; }

    public IReadOnlyCollection<DeploymentResourceId> Dependencies { get; }

    public DeploymentDeletionBehavior Deletion { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = DeploymentEmpty.StringDictionary;
}
