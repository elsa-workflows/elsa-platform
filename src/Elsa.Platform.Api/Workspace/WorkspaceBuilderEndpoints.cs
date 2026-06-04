using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Public.Builder;
using Elsa.Platform.Api.Public.Compatibility;
using Elsa.Platform.Api.Public.Packages;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using Elsa.Platform.RuntimeBuilder.Abstractions.Planner;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageManifests.Compatibility;
using Elsa.Platform.RuntimeBuilder.Core.Builder;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Planner;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Platform.Api.Workspace;

public static class WorkspaceBuilderEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceBuilderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var builder = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/builder")
            .WithTags("Workspace Runtime Builder");

        builder.MapGet("/catalog", async (
            Guid workspaceId,
            [FromQuery] Guid[] sourceIds,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            PublicCatalogQueryService catalog,
            RuntimeImageCatalog runtimeImages,
            InfrastructureProviderCatalog infrastructure,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var packages = await catalog.ListPackagesForWorkspaceAsync(workspaceId, sourceIds, cancellationToken);
            var serverPackages = RuntimeKindCompatibilityPolicy.FilterPackages(packages, ElsaRuntimeKinds.Server);
            return Results.Ok(new BuilderCatalogResponse(
                runtimeImages.ListImages().Select(BuilderEndpoints.ToRuntimeImageResponse).ToList(),
                serverPackages.Select(PublicPackageEndpoints.ToResponse).ToList(),
                infrastructure.ListProviders().Select(ToResponse).ToList()));
        });

        builder.MapPost("/resolve", async (
            Guid workspaceId,
            BuilderResolveRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            CompatibilityCheckService compatibility,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

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
        });

        builder.MapPost("/plan", async (
            Guid workspaceId,
            BuilderPlanApiRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            BuilderPlannerService planner,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var result = await planner.PlanAsync(new BuilderPlanRequest(request.Intent), workspaceId, cancellationToken);
            return Results.Ok(BuilderEndpoints.ToResponse(result));
        });

        builder.MapPost("/bundle", async (
            Guid workspaceId,
            BuilderBundleRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            BundleGenerationService bundles,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            if (!BuilderEndpoints.TryMapIntent(request, out var intent, out var error))
                return Results.BadRequest(new { error });

            var result = await bundles.GenerateAsync(intent, workspaceId, cancellationToken);
            return Results.Ok(BuilderEndpoints.ToResponse(result));
        });

        endpoints.MapPost("/api/workspaces/{workspaceId:guid}/compatibility/check", async (
            Guid workspaceId,
            CompatibilityCheckApiRequest request,
            HttpContext context,
            WorkspaceAccessResolver accessResolver,
            CompatibilityCheckService compatibility,
            CancellationToken cancellationToken) =>
        {
            var access = await accessResolver.ResolveAsync(context, workspaceId, WorkspaceOperation.Read, cancellationToken);
            if (!access.Succeeded)
                return access.ToHttpResult();

            var result = await compatibility.CheckAsync(new CompatibilityCheckRequest(
                request.ElsaVersion,
                request.DockerImageVersion,
                request.Packages.Select(x => new SelectedPackageVersion(x.SourceId, x.PackageId, x.Version)).ToList(),
                request.Features ?? [],
                workspaceId), cancellationToken);

            return Results.Ok(new CompatibilityCheckApiResponse(
                result.Compatible,
                result.Findings.Select(x => new CompatibilityFindingApiResponse(x.Severity, x.Code, x.Message)).ToList()));
        }).WithTags("Workspace Compatibility");

        return endpoints;
    }

    private static BuilderInfrastructureProviderResponse ToResponse(InfrastructureProvider provider) =>
        new(provider.Id, provider.DisplayName, provider.Kind, provider.Strategy, provider.Provider, provider.Capabilities, provider.Outputs);
}
