namespace ElsaControl.Deployment.Artifacts;

public interface IDeploymentArtifactBuilder
{
    ValueTask<DeploymentArtifactBuildResult> BuildFolderAsync(
        DeploymentArtifactBuildOptions options,
        CancellationToken cancellationToken = default);

    ValueTask<DeploymentArtifactBuildResult> BuildZipAsync(
        DeploymentArtifactBuildOptions options,
        CancellationToken cancellationToken = default);
}
