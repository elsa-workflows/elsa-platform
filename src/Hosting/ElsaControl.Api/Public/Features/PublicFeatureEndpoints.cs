using ElsaControl.Api.Public;
using ElsaControl.PackageCatalog.Abstractions.Catalog;
using ElsaControl.PackageCatalog.Core.Packages;

namespace ElsaControl.Api.Public.Features;

public static class PublicFeatureEndpoints
{
    public static IEndpointRouteBuilder MapPublicFeatureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/features").WithTags("Public Features");

        group.MapGet("/", async (PublicCatalogQueryService catalog, CancellationToken cancellationToken) =>
        {
            var features = await catalog.ListFeaturesAsync(cancellationToken);
            return Results.Ok(features.Select(ToResponse));
        });

        group.MapGet("/{featureId}", async (string featureId, PublicCatalogQueryService catalog, CancellationToken cancellationToken) =>
        {
            var feature = await catalog.GetFeatureAsync(featureId, cancellationToken);
            return feature is null ? Results.NotFound() : Results.Ok(ToResponse(feature));
        });

        return endpoints;
    }

    private static PublicFeatureResponse ToResponse(PublicFeatureProjection feature) =>
        new(
            feature.FeatureId,
            feature.PackageId,
            feature.PackageVersion,
            new PublicFeatureSourceResponse(feature.Source.Id, feature.Source.Name, feature.Source.Url),
            feature.TypeName,
            feature.DisplayName,
            feature.Description,
            feature.Category,
            feature.Categories,
            feature.RuntimeKinds,
            feature.RequiredCapabilities,
            feature.Dependencies.Select(x => new PublicFeatureDependencyResponse(x.PackageId, x.VersionRange, x.FeatureId, x.Optional, x.Reason)).ToList(),
            feature.Conflicts.Select(x => new PublicFeatureConflictResponse(x.PackageId, x.VersionRange, x.FeatureId, x.Reason)).ToList(),
            feature.Infrastructure.Select(x => new PublicFeatureInfrastructureRequirementResponse(x.Id, x.Kind, x.Optional, x.Reason, x.Capabilities, x.Providers, x.ConfigurationKeys, PublicJsonMetadata.ParseObject(x.ExtensionsJson))).ToList(),
            feature.Advanced,
            feature.Experimental,
            PublicJsonMetadata.ParseObject(feature.ExtensionsJson),
            feature.Settings.Select(ToResponse).ToList());

    private static PublicFeatureSettingResponse ToResponse(PublicFeatureSettingProjection setting) =>
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
