using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;

namespace ValenceControl.Api.Workspace;

public sealed class WorkspacePermissionOwnerProvisioner(WorkspacePermissionService permissions) : IWorkspaceOwnerProvisioner
{
    public async Task ProvisionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
        await permissions.BootstrapOwnerPermissionsAsync(workspaceId, accountId, cancellationToken);
}
