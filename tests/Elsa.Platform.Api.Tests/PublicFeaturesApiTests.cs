using System.Net;
using System.Net.Http.Json;
using Elsa.Platform.Api.Public.Features;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class PublicFeaturesApiTests
{
    [Fact]
    public async Task Get_features_returns_visible_features()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var features = await app.CreateClient().GetFromJsonAsync<List<PublicFeatureResponse>>("/api/features");

        features.Should().ContainSingle(x => x.FeatureId == "email" && x.PackageId == "Elsa.Email");
    }

    [Fact]
    public async Task Get_feature_returns_not_found_when_package_is_hidden()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source, approved: false);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().GetAsync("/api/features/email");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_feature_ignores_malformed_default_value_json()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            var feature = PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            feature.Settings[0].DefaultValueJson = "{bad";

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var feature = await app.CreateClient().GetFromJsonAsync<PublicFeatureResponse>("/api/features/email");

        feature!.Settings[0].DefaultValue.Should().BeNull();
    }
}
