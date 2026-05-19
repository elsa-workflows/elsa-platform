using Elsa.Platform.PackageCatalog.Api.Public;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Platform.PackageCatalog.Api.Public.Packages;

public static class PublicPackageEndpoints
{
    public static IEndpointRouteBuilder MapPublicPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/packages").WithTags("Public Packages");

        group.MapGet("/", async ([FromQuery] Guid[] sourceIds, PublicCatalogQueryService catalog, CancellationToken cancellationToken) =>
        {
            var packages = await catalog.ListPackagesAsync(sourceIds, cancellationToken);
            return Results.Ok(packages.Select(ToResponse));
        });

        endpoints.MapGet("/api/sources/{sourceId:guid}/packages/{packageId}", async (Guid sourceId, string packageId, PublicCatalogQueryService catalog, CancellationToken cancellationToken) =>
        {
            var package = await catalog.GetPackageAsync(sourceId, packageId, cancellationToken);
            return package is null ? Results.NotFound() : Results.Ok(ToResponse(package));
        }).WithTags("Public Packages");

        endpoints.MapGet("/api/sources/{sourceId:guid}/packages/{packageId}/versions", async (Guid sourceId, string packageId, PublicCatalogQueryService catalog, CancellationToken cancellationToken) =>
        {
            var versions = await catalog.ListVersionsAsync(sourceId, packageId, cancellationToken);
            return Results.Ok(versions.Select(ToResponse));
        }).WithTags("Public Packages");

        endpoints.MapGet("/api/sources/{sourceId:guid}/packages/{packageId}/versions/{version}", async (Guid sourceId, string packageId, string version, PublicCatalogQueryService catalog, CancellationToken cancellationToken) =>
        {
            var packageVersion = await catalog.GetVersionAsync(sourceId, packageId, version, cancellationToken);
            return packageVersion is null ? Results.NotFound() : Results.Ok(ToResponse(packageVersion));
        }).WithTags("Public Packages");

        return endpoints;
    }

    public static PublicPackageResponse ToResponse(PublicPackageProjection package) =>
        new(package.PackageId, package.DisplayName, ToResponse(package.Source), package.LatestVersion, package.Versions.Select(ToResponse).ToList());

    public static PublicPackageVersionResponse ToResponse(PublicPackageVersionProjection version) =>
        new(version.PackageId, version.Version, ToResponse(version.Source), version.SchemaVersion, version.PublishedAt, version.Features.Select(ToResponse).ToList());

    public static PublicPackageSourceResponse ToResponse(PublicPackageSourceProjection source) =>
        new(source.Id, source.Name, source.Url);

    public static PublicPackageFeatureResponse ToResponse(PublicFeatureProjection feature) =>
        new(
            feature.FeatureId,
            feature.TypeName,
            feature.DisplayName,
            feature.Description,
            feature.Category,
            feature.RequiredCapabilities,
            feature.Dependencies.Select(x => new PublicPackageDependencyResponse(x.PackageId, x.VersionRange, x.FeatureId, x.Optional, x.Reason)).ToList(),
            feature.Conflicts.Select(x => new PublicPackageConflictResponse(x.PackageId, x.VersionRange, x.FeatureId, x.Reason)).ToList(),
            feature.Infrastructure.Select(x => new PublicPackageInfrastructureRequirementResponse(x.Id, x.Kind, x.Optional, x.Reason, x.Capabilities, x.Providers, x.ConfigurationKeys, PublicJsonMetadata.ParseObject(x.ExtensionsJson))).ToList(),
            feature.Advanced,
            feature.Experimental,
            PublicJsonMetadata.ParseObject(feature.ExtensionsJson),
            feature.Settings.Select(ToResponse).ToList());

    public static PublicPackageFeatureSettingResponse ToResponse(PublicFeatureSettingProjection setting) =>
        new(
            setting.Name,
            setting.ClrType,
            setting.JsonType,
            setting.Required,
            PublicJsonMetadata.ParseValue(setting.DefaultValueJson),
            setting.DisplayName,
            setting.Description,
            setting.Category,
            PublicJsonMetadata.ParseObject(setting.ValidationJson),
            setting.Secret,
            setting.RestartRequired,
            setting.EnvironmentVariable,
            PublicJsonMetadata.ParseObject(setting.UiJson),
            PublicJsonMetadata.ParseObject(setting.ExtensionsJson));
}
