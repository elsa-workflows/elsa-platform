namespace ValenceControl.Deployment.Abstractions.Artifacts;

/// <summary>
/// Writes deployment artifact metadata and content without assuming a storage format.
/// </summary>
public interface IArtifactWriter
{
    ValueTask WriteMetadataAsync(DeploymentArtifactMetadata metadata, CancellationToken cancellationToken = default);

    ValueTask WriteAsync(string path, Stream content, CancellationToken cancellationToken = default);
}
