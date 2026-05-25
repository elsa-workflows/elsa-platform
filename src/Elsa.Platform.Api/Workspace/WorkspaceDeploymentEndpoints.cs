using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.Api.Workspace;

public static class WorkspaceDeploymentEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/deployments")
            .WithTags("Workspace Deployments");

        group.MapGet("/permissions", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var effectivePermissions = access.Access!.Role is WorkspaceRole.Owner
                ? WorkspaceDeploymentPermissions.All
                : (await permissions.GetEffectivePermissionsAsync(workspaceId, access.Access.AccountId, cancellationToken)).Permissions;

            return Results.Ok(new WorkspaceDeploymentPermissionsResponse(effectivePermissions.OrderBy(x => x, StringComparer.Ordinal).ToList()));
        });

        group.MapGet("/cockpit", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            WorkspacePermissionService permissions,
            DeploymentCockpitService cockpit,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            if (access.Access!.Role is not WorkspaceRole.Owner)
            {
                var effective = await permissions.GetEffectivePermissionsAsync(workspaceId, access.Access.AccountId, cancellationToken);
                if (!effective.Has(WorkspaceDeploymentPermissions.Read))
                    return Results.Problem(
                        title: "Deployment read permission is required.",
                        statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(await cockpit.GetCockpitAsync(workspaceId, cancellationToken));
        });

        return endpoints;
    }
}
