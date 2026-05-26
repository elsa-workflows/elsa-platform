namespace Elsa.Platform.Deployment.Core.Workspace;

public interface IWorkspaceArtifactStore
{
    Task<IReadOnlyList<WorkspaceArtifact>> ListArtifactsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifact?> GetArtifactAsync(
        Guid workspaceId,
        Guid artifactRecordId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifact?> FindArtifactByIdentityAsync(
        Guid workspaceId,
        string artifactId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifact> RegisterArtifactAsync(
        Guid workspaceId,
        RegisterWorkspaceArtifactRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceArtifactInspectionResult> UpdateArtifactInspectionAsync(
        Guid workspaceId,
        WorkspaceArtifactInspectionUpdate update,
        CancellationToken cancellationToken = default);
}
