using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.PackageCatalog.Api.Workspace;

public static class WorkspaceDeploymentEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceDeploymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/deployments")
            .WithTags("Workspace Deployments");

        group.MapGet("/cockpit", async (
            Guid workspaceId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            DeploymentCockpitService cockpit,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            return Results.Ok(await cockpit.GetCockpitAsync(workspaceId, cancellationToken));
        });

        return endpoints;
    }
}
