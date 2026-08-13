using ValenceControl.PackageCatalog.Abstractions.Compatibility;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.Api.Public.Compatibility;
using ValenceControl.Api.Public.Packages;
using ValenceControl.RuntimeBuilder.Abstractions;
using ValenceControl.RuntimeBuilder.Abstractions.Planner;
using ValenceControl.PackageCatalog.Core.Compatibility;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.RuntimeBuilder.Core.Builder;
using ValenceControl.RuntimeBuilder.Core.Builder.Planner;
using Microsoft.AspNetCore.Mvc;

namespace ValenceControl.Api.Workspace;

public static class WorkspaceBuilderEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceBuilderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var builder = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/builder")
            .WithTags("Workspace Runtime Builder");

        builder.MapGet("/catalog", async (
            Guid workspaceId,
            [FromQuery] Guid[] sourceIds,
            PublicCatalogQueryService catalog,
            RuntimeImageCatalog runtimeImages,
            InfrastructureProviderCatalog infrastructure,
            CancellationToken cancellationToken) =>
        {
            var packages = await catalog.ListPackagesForWorkspaceAsync(workspaceId, sourceIds, cancellationToken);
            return Results.Ok(new BuilderCatalogResponse(
                runtimeImages.ListImages().Select(BuilderEndpoints.ToRuntimeImageResponse).ToList(),
                packages.Select(PublicPackageEndpoints.ToResponse).ToList(),
                infrastructure.ListProviders().Select(ToResponse).ToList()));
        }).RequireWorkspaceAccess();

        builder.MapPost("/resolve", async (
            Guid workspaceId,
            BuilderResolveRequest request,
            CompatibilityCheckService compatibility,
            CancellationToken cancellationToken) =>
        {
            if (request.Packages is null)
                return Results.BadRequest(new { error = "packages is required." });

            var features = request.Features ?? request.Packages
                .SelectMany(x => x.SelectedFeatures ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = await compatibility.CheckAsync(new CompatibilityCheckRequest(
                request.ElsaVersion,
                request.DockerImageVersion,
                request.Packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
                features,
                workspaceId), cancellationToken);

            return Results.Ok(new BuilderResolveResponse(
                result.Compatible,
                result.Findings.Select(x => new CompatibilityFindingApiResponse(x.Severity, x.Code, x.Message)).ToList()));
        }).RequireWorkspaceAccess();

        builder.MapPost("/plan", async (
            Guid workspaceId,
            BuilderPlanApiRequest request,
            BuilderPlannerService planner,
            CancellationToken cancellationToken) =>
        {
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var result = await planner.PlanAsync(new BuilderPlanRequest(request.Intent), workspaceId, cancellationToken);
            return Results.Ok(BuilderEndpoints.ToResponse(result));
        }).RequireWorkspaceAccess();

        builder.MapPost("/bundle", async (
            Guid workspaceId,
            BuilderBundleRequest request,
            BundleGenerationService bundles,
            CancellationToken cancellationToken) =>
        {
            if (!BuilderEndpoints.TryMapIntent(request, out var intent, out var error))
                return Results.BadRequest(new { error });

            var result = await bundles.GenerateAsync(intent, workspaceId, cancellationToken);
            return Results.Ok(BuilderEndpoints.ToResponse(result));
        }).RequireWorkspaceAccess();

        endpoints.MapPost("/api/workspaces/{workspaceId:guid}/compatibility/check", async (
            Guid workspaceId,
            CompatibilityCheckApiRequest request,
            CompatibilityCheckService compatibility,
            CancellationToken cancellationToken) =>
        {
            var result = await compatibility.CheckAsync(new CompatibilityCheckRequest(
                request.ElsaVersion,
                request.DockerImageVersion,
                request.Packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
                request.Features ?? [],
                workspaceId), cancellationToken);

            return Results.Ok(new CompatibilityCheckApiResponse(
                result.Compatible,
                result.Findings.Select(x => new CompatibilityFindingApiResponse(x.Severity, x.Code, x.Message)).ToList()));
        }).RequireWorkspaceAccess().WithTags("Workspace Compatibility");

        return endpoints;
    }

    private static BuilderInfrastructureProviderResponse ToResponse(InfrastructureProvider provider) =>
        new(provider.Id, provider.DisplayName, provider.Kind, provider.Strategy, provider.Provider, provider.Capabilities, provider.Outputs);
}
