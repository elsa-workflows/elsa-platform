using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Workspace;

public sealed class WorkspacePermissionOwnerProvisioner(WorkspacePermissionService permissions) : IWorkspaceOwnerProvisioner
{
    public async Task ProvisionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
        await permissions.BootstrapOwnerPermissionsAsync(workspaceId, accountId, cancellationToken);
}
