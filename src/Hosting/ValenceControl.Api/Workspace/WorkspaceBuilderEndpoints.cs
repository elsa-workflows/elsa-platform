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
            ILogger<BuilderPlannerService> logger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request.Intent is null)
                return BuilderEndpoints.BuilderProblem(
                    httpContext,
                    "urn:valence-control:problem:builder-plan-invalid-request",
                    "Invalid builder plan request",
                    "builder.plan.invalid_request",
                    "intent is required.",
                    StatusCodes.Status400BadRequest);

            try
            {
                var result = await planner.PlanAsync(new BuilderPlanRequest(request.Intent), workspaceId, cancellationToken);
                return Results.Ok(BuilderEndpoints.ToResponse(result));
            }
            catch (BuilderPlanningTimeoutException exception)
            {
                return BuilderEndpoints.PlanningTimeoutProblem(exception, httpContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Workspace builder planning failed workspaceId={WorkspaceId} traceId={TraceId}", workspaceId, httpContext.TraceIdentifier);
                return BuilderEndpoints.BuilderProblem(
                    httpContext,
                    "urn:valence-control:problem:builder-plan-failed",
                    "Builder planning failed",
                    "builder.plan.failed",
                    "The builder plan could not be completed. Use the traceId to correlate this response with server logs.",
                    StatusCodes.Status500InternalServerError);
            }
        }).RequireWorkspaceAccess();

        builder.MapPost("/bundle", async (
            Guid workspaceId,
            BuilderBundleRequest request,
            BundleGenerationService bundles,
            ILogger<BundleGenerationService> logger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!BuilderEndpoints.TryMapIntent(request, out var intent, out var error))
                return BuilderEndpoints.BuilderProblem(
                    httpContext,
                    "urn:valence-control:problem:builder-bundle-invalid-request",
                    "Invalid bundle request",
                    "builder.bundle.invalid_request",
                    error,
                    StatusCodes.Status400BadRequest);

            try
            {
                var result = await bundles.GenerateAsync(intent, workspaceId, cancellationToken);
                return Results.Ok(BuilderEndpoints.ToResponse(result));
            }
            catch (BuilderPlanningTimeoutException exception)
            {
                return BuilderEndpoints.PlanningTimeoutProblem(exception, httpContext);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Workspace bundle generation failed workspaceId={WorkspaceId} traceId={TraceId}", workspaceId, httpContext.TraceIdentifier);
                return BuilderEndpoints.BuilderProblem(
                    httpContext,
                    "urn:valence-control:problem:builder-bundle-generation-failed",
                    "Bundle generation failed",
                    "builder.bundle.failed",
                    "The bundle could not be generated. Use the traceId to correlate this response with server logs.",
                    StatusCodes.Status500InternalServerError);
            }
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
