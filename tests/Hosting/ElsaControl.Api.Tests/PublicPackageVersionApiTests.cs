using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Public.Packages;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class PublicPackageVersionApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public PublicPackageVersionApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Get_version_returns_feature_settings()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var version = await app.CreateClient().GetFromJsonAsync<PublicPackageVersionResponse>($"/api/sources/{sourceId}/packages/Elsa.Email/versions/1.0.0");

        Assert.Single(version!.Features, x => x.FeatureId == "email");
        Assert.Single(version.Features[0].Settings, x => x.Name == "smtpHost");
    }

    [Fact]
    public async Task Get_hidden_version_returns_not_found()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package, listed: false);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().GetAsync($"/api/sources/{sourceId}/packages/Elsa.Email/versions/1.0.0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_undeclared_version_projects_empty_runtime_kinds()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var version = await app.CreateClient().GetFromJsonAsync<PublicPackageVersionResponse>($"/api/sources/{sourceId}/packages/Elsa.Email/versions/1.0.0");

        Assert.Empty(version!.RuntimeKinds);
        Assert.Empty(version.Features[0].RuntimeKinds);
    }
}
