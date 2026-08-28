using System.Net.Http.Json;
using ElsaControl.Api.Public.Packages;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class PublicPackageDetailsApiTests
{
    [Fact]
    public async Task Get_versions_returns_visible_versions_for_package()
    {
        await using var app = new ControlApiTestApplication();
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

        var versions = await app.CreateClient().GetFromJsonAsync<List<PublicPackageVersionResponse>>($"/api/sources/{sourceId}/packages/Elsa.Email/versions");

        Assert.Single(versions!, x => x.Version == "1.0.0");
    }
}
