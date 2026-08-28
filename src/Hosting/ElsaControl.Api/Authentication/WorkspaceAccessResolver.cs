using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Authentication;

public sealed class WorkspaceAccessResolver(
    IWorkspaceIdentityReader identityReader,
    AccountWorkspaceService accounts)
{
    public async Task<WorkspaceAccessResult> ResolveAsync(
        HttpContext context,
        Guid workspaceId,
        WorkspaceOperation operation,
        CancellationToken cancellationToken = default)
    {
        var identity = await identityReader.ReadAsync(context);
        if (identity is null)
            return WorkspaceAccessResult.Denied(WorkspaceAccessFailure.MissingIdentity);

        var access = await accounts.GetWorkspaceAccessAsync(identity, workspaceId, cancellationToken);
        if (access is null)
            return WorkspaceAccessResult.Denied(WorkspaceAccessFailure.WorkspaceNotAllowed);

        return WorkspaceRolePolicy.Allows(access.Role, operation)
            ? WorkspaceAccessResult.Success(access)
            : WorkspaceAccessResult.Denied(WorkspaceAccessFailure.RoleNotAllowed);
    }
}
