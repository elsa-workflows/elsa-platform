using System.Net;
using System.Net.Http.Json;
using Elsa.Platform.Api.Public.Packages;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class PublicPackageVersionApiTests
{
    [Fact]
    public async Task Get_version_returns_feature_settings()
    {
        await using var app = new PlatformApiTestApplication();
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

        version!.Features.Should().ContainSingle(x => x.FeatureId == "email");
        version.Features[0].Settings.Should().ContainSingle(x => x.Name == "smtpHost");
    }

    [Fact]
    public async Task Get_hidden_version_returns_not_found()
    {
        await using var app = new PlatformApiTestApplication();
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

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
