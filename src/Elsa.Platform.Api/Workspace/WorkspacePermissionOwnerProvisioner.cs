using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace;

public sealed class WorkspacePermissionOwnerProvisioner(WorkspacePermissionService permissions) : IWorkspaceOwnerProvisioner
{
    public async Task ProvisionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
        await permissions.BootstrapOwnerPermissionsAsync(workspaceId, accountId, cancellationToken);
}
