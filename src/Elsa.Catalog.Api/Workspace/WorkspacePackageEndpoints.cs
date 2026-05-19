using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Api.Public.Packages;
using Elsa.Catalog.Core.Accounts;
using Elsa.Catalog.Core.Packages;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Catalog.Api.Workspace;

public static class WorkspacePackageEndpoints
{
    public static IEndpointRouteBuilder MapWorkspacePackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var packages = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/packages")
            .WithTags("Workspace Packages");

        packages.MapGet("/", async (
            Guid workspaceId,
            [FromQuery] Guid[] sourceIds,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var result = await catalog.ListPackagesForWorkspaceAsync(workspaceId, sourceIds, cancellationToken);
            return Results.Ok(result.Select(PublicPackageEndpoints.ToResponse));
        });

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var package = await catalog.GetPackageForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);
            return package is null ? Results.NotFound() : Results.Ok(PublicPackageEndpoints.ToResponse(package));
        }).WithTags("Workspace Packages");

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}/versions", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var versions = await catalog.ListVersionsForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);
            return Results.Ok(versions.Select(PublicPackageEndpoints.ToResponse));
        }).WithTags("Workspace Packages");

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}/versions/{version}", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            string version,
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await WorkspaceSourceEndpoints.GetAccessAsync(context, workspaceId, identityReader, accounts, cancellationToken);
            if (access.Result is not null)
                return access.Result;

            var packageVersion = await catalog.GetVersionForWorkspaceAsync(workspaceId, sourceId, packageId, version, cancellationToken);
            return packageVersion is null ? Results.NotFound() : Results.Ok(PublicPackageEndpoints.ToResponse(packageVersion));
        }).WithTags("Workspace Packages");

        return endpoints;
    }
}
