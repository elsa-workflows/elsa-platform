using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Public.Compatibility;
using Elsa.Platform.PackageCatalog.Api.Public.Packages;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using Elsa.Platform.RuntimeBuilder.Abstractions.Planner;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.RuntimeBuilder.Core.Builder;
using Elsa.Platform.RuntimeBuilder.Core.Builder.Planner;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Platform.PackageCatalog.Api.Public.Builder;

public static class BuilderEndpoints
{
    public static IEndpointRouteBuilder MapBuilderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/builder").WithTags("Runtime Builder");

        group.MapGet("/catalog", async ([FromQuery] Guid[] sourceIds, PublicCatalogQueryService catalog, RuntimeImageCatalog runtimeImages, InfrastructureProviderCatalog infrastructure, CancellationToken cancellationToken) =>
        {
            var packages = await catalog.ListPackagesAsync(sourceIds, cancellationToken);
            return Results.Ok(new BuilderCatalogResponse(
                runtimeImages.ListImages().Select(ToRuntimeImageResponse).ToList(),
                packages.Select(PublicPackageEndpoints.ToResponse).ToList(),
                infrastructure.ListProviders().Select(ToResponse).ToList()));
        });

        group.MapGet("/infrastructure/providers", (InfrastructureProviderCatalog infrastructure) =>
            Results.Ok(infrastructure.ListProviders().Select(ToResponse)));

        group.MapPost("/resolve", async (BuilderResolveRequest request, CompatibilityCheckService compatibility, CancellationToken cancellationToken) =>
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
                features), cancellationToken);

            return Results.Ok(new BuilderResolveResponse(
                result.Compatible,
                result.Findings.Select(x => new CompatibilityFindingApiResponse(x.Severity, x.Code, x.Message)).ToList()));
        });

        group.MapPost("/plan", async (BuilderPlanApiRequest request, BuilderPlannerService planner, CancellationToken cancellationToken) =>
        {
            if (request.Intent is null)
                return Results.BadRequest(new { error = "intent is required." });

            var result = await planner.PlanAsync(new BuilderPlanRequest(request.Intent), cancellationToken: cancellationToken);
            return Results.Ok(ToResponse(result));
        });

        group.MapPost("/bundle", async (BuilderBundleRequest request, BundleGenerationService bundles, CancellationToken cancellationToken) =>
        {
            if (!TryMapIntent(request, out var intent, out var error))
                return Results.BadRequest(new { error });

            var result = await bundles.GenerateAsync(intent, cancellationToken: cancellationToken);
            return Results.Ok(ToResponse(result));
        }).RequireAuthorization(BuilderClientAuthorization.Policy);

        return endpoints;
    }

    internal static bool TryMapIntent(BuilderBundleRequest request, out RuntimeBuilderIntent intent, out string error)
    {
        intent = null!;
        error = "";
        if (request.Image is null || string.IsNullOrWhiteSpace(request.Image.Slug))
        {
            error = "image.slug is required.";
            return false;
        }

        if (request.Packages is null)
        {
            error = "packages is required.";
            return false;
        }

        intent = new RuntimeBuilderIntent(
            new RuntimeImageSelection(
                request.Image.Slug,
                request.Image.Tag,
                request.Image.HostPort,
                request.Image.EnvOverrides),
            request.Packages.Select(package => new BundlePackageSelection(
                package.SourceId,
                package.PackageId ?? "",
                package.Version ?? "",
                package.SelectedFeatures,
                package.Settings)).ToList(),
            (request.PackageSources ?? []).Select(source => new PackageSourceSelection(source.SourceId, source.Name, source.Url, source.Kind)).ToList(),
            (request.Infrastructure ?? []).Select(provider => new InfrastructureSelection(
                provider.Kind ?? "",
                provider.ProviderId ?? "",
                provider.Strategy ?? "",
                provider.Settings)).ToList(),
            request.LocalPackages is null ? null : new LocalPackagesOptions(request.LocalPackages.Enabled, request.LocalPackages.DirectoryPath),
            request.Target);
        return true;
    }

    internal static BuilderBundleResponse ToResponse(BundleGenerationResult result) =>
        new(
            result.BundleId,
            result.Files.Select(file => new BuilderBundleFileResponse(file.Path, file.Language, file.ContentType, file.Required, file.Contents)).ToList(),
            result.Findings.Select(finding => new BuilderBundleFindingResponse(finding.Level, finding.Code, finding.Message, finding.Scope)).ToList());

    internal static BuilderPlanApiResponse ToResponse(BuilderPlanResult result) =>
        new(
            result.Resolved,
            new BuilderPlanAutoAddedApiResponse(result.AutoAdded.Packages, result.AutoAdded.Features, result.AutoAdded.Infrastructure),
            result.Findings.Select(finding => new BuilderBundleFindingResponse(finding.Level, finding.Code, finding.Message, finding.Scope)).ToList());

    private static BuilderInfrastructureProviderResponse ToResponse(InfrastructureProvider provider) =>
        new(provider.Id, provider.DisplayName, provider.Kind, provider.Strategy, provider.Provider, provider.Capabilities, provider.Outputs);

    internal static RuntimeImageResponse ToRuntimeImageResponse(RuntimeImage image) =>
        new(
            image.Slug,
            image.DisplayName,
            image.Description,
            image.Image,
            image.AvailableTags,
            image.DefaultTag,
            image.DefaultPort,
            image.HostPort,
            image.ContainerName,
            image.LicenseTier,
            image.Stability,
            image.Capabilities,
            image.EnvVars.Select(x => new RuntimeImageEnvironmentVariableResponse(x.Name, x.DisplayName, x.Description, x.Required, x.Secret, x.DefaultValue, x.Group, x.Advanced)).ToList(),
            new RuntimeImageDeploymentHintsResponse(
                image.DeploymentHints.SupportsDockerCompose,
                image.DeploymentHints.SupportsKubernetes,
                image.DeploymentHints.RequiresCompanionServer,
                image.DeploymentHints.NeedsSharedNetwork,
                image.DeploymentHints.CompanionImageSlug),
            new RuntimeImageDocsResponse(image.Docs.DockerHubUrl, image.Docs.ContainerPaths, image.Docs.ShowPerShellAdmin, image.Docs.ShowNuplane));
}
