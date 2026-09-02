using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Public.Features;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class PublicFeaturesApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public PublicFeaturesApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Get_features_returns_visible_features()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var features = await app.CreateClient().GetFromJsonAsync<List<PublicFeatureResponse>>("/api/features");

        Assert.Single(features!, x => x.FeatureId == "email" && x.PackageId == "Elsa.Email");
    }

    [Fact]
    public async Task Get_feature_returns_not_found_when_package_is_hidden()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source, approved: false);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().GetAsync("/api/features/email");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_feature_ignores_malformed_default_value_json()
    {
        var app = _app;
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

        Assert.Null(feature!.Settings[0].DefaultValue);
    }
}
