using ElsaControl.Api.Authentication;
using ElsaControl.Api.Public.Packages;
using ElsaControl.PackageCatalog.Core.Packages;
using Microsoft.AspNetCore.Mvc;

namespace ElsaControl.Api.Workspace;

public static class WorkspacePackageEndpoints
{
    public static IEndpointRouteBuilder MapWorkspacePackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var packages = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/packages")
            .WithTags("Workspace Packages");

        packages.MapGet("/", async (
            Guid workspaceId,
            [FromQuery] Guid[] sourceIds,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var result = await catalog.ListPackagesForWorkspaceAsync(workspaceId, sourceIds, cancellationToken);
            return Results.Ok(result.Select(PublicPackageEndpoints.ToResponse));
        }).RequireWorkspaceAccess();

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var package = await catalog.GetPackageForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);
            return package is null ? Results.NotFound() : Results.Ok(PublicPackageEndpoints.ToResponse(package));
        }).RequireWorkspaceAccess().WithTags("Workspace Packages");

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}/versions", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var versions = await catalog.ListVersionsForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);
            return Results.Ok(versions.Select(PublicPackageEndpoints.ToResponse));
        }).RequireWorkspaceAccess().WithTags("Workspace Packages");

        endpoints.MapGet("/api/workspaces/{workspaceId:guid}/sources/{sourceId:guid}/packages/{packageId}/versions/{version}", async (
            Guid workspaceId,
            Guid sourceId,
            string packageId,
            string version,
            PublicCatalogQueryService catalog,
            CancellationToken cancellationToken) =>
        {
            var packageVersion = await catalog.GetVersionForWorkspaceAsync(workspaceId, sourceId, packageId, version, cancellationToken);
            return packageVersion is null ? Results.NotFound() : Results.Ok(PublicPackageEndpoints.ToResponse(packageVersion));
        }).RequireWorkspaceAccess().WithTags("Workspace Packages");

        return endpoints;
    }
}
