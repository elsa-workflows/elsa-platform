namespace Elsa.Platform.Deployment.Artifacts;

public interface IDeploymentArtifactReader
{
    ValueTask<DeploymentArtifactInspectionResult> InspectFolderAsync(
        string path,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentArtifactInspectionResult> InspectZipAsync(
        string path,
        CancellationToken cancellationToken = default);
}
