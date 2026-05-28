namespace Elsa.Platform.Deployment.Core.Workspace;

public interface IWorkspacePermissionStore
{
    Task<IReadOnlyList<WorkspacePermissionGrant>> GetPermissionGrantsAsync(
        Guid workspaceId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<WorkspacePermissionGrant> GrantPermissionAsync(
        Guid workspaceId,
        GrantWorkspacePermissionRequest request,
        CancellationToken cancellationToken = default);
}
