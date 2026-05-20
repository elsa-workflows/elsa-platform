using Elsa.Platform.Deployment.Abstractions.Resources;

namespace Elsa.Platform.Deployment.Abstractions.Artifacts;

/// <summary>
/// Reads deployment artifact metadata and content without assuming a storage format.
/// </summary>
public interface IArtifactReader
{
    ValueTask<DeploymentArtifactMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<DeploymentResource>> ReadResourcesAsync(CancellationToken cancellationToken = default);

    ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);
}
