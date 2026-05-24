using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Public.Packages;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Platform.Api.Workspace;

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
            WorkspaceAccessResolver accessResolver,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var result = await catalog.ListPackagesForWorkspaceAsync(workspaceId, sourceIds, cancellationToken);
            return Results.Ok(result.Select(PublicPackageEndpoints.ToResponse));
        });

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var package = await catalog.GetPackageForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);
            return package is null ? Results.NotFound() : Results.Ok(PublicPackageEndpoints.ToResponse(package));
        }).WithTags("Workspace Packages");

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}/versions", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var versions = await catalog.ListVersionsForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);
            return Results.Ok(versions.Select(PublicPackageEndpoints.ToResponse));
        }).WithTags("Workspace Packages");

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}/versions/{version}", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            string version,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var packageVersion = await catalog.GetVersionForWorkspaceAsync(workspaceId, sourceId, packageId, version, cancellationToken);
            return packageVersion is null ? Results.NotFound() : Results.Ok(PublicPackageEndpoints.ToResponse(packageVersion));
        }).WithTags("Workspace Packages");

        return endpoints;
    }
}
