using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace;

/// <summary>
/// Endpoint filters that resolve workspace access and enforce a deployment permission
/// (or workspace ownership) before the handler runs.
/// </summary>
public static class DeploymentPermissionEndpointFilters
{
    public static RouteHandlerBuilder RequireDeploymentPermission(this RouteHandlerBuilder builder, string permission) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var denied = await WorkspaceAccessEndpointFilters.ResolveWorkspaceAccessAsync(httpContext, WorkspaceOperation.Read);
            if (denied is not null)
                return denied;

            var permissions = httpContext.RequestServices.GetRequiredService<WorkspacePermissionService>();
            return await permissions.HasDeploymentPermissionAsync(httpContext.GetWorkspaceAccess(), permission, httpContext.RequestAborted)
                ? await next(context)
                : DeploymentPermissionDenied();
        });

    public static RouteHandlerBuilder RequireDeploymentOwner(this RouteHandlerBuilder builder) =>
        builder.RequireWorkspaceOwner(DeploymentPermissionDenied);

    public static IResult DeploymentPermissionDenied() =>
        Results.Problem(
            title: "Deployment permission is required.",
            statusCode: StatusCodes.Status403Forbidden);

    public static async Task<bool> HasDeploymentPermissionAsync(
        this WorkspacePermissionService permissions,
        WorkspaceAccess access,
        string permission,
        CancellationToken cancellationToken) =>
        (await permissions.GetEffectiveDeploymentPermissionsAsync(access, cancellationToken)).Has(permission);

    public static Task<EffectiveWorkspacePermissions> GetEffectiveDeploymentPermissionsAsync(
        this WorkspacePermissionService permissions,
        WorkspaceAccess access,
        CancellationToken cancellationToken) =>
        permissions.GetEffectivePermissionsAsync(access.WorkspaceId, access.AccountId, cancellationToken);
}
