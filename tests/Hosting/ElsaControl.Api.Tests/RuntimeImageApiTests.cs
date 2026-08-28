using System.Net.Http.Json;
using ElsaControl.Api.Public.Builder;

namespace ElsaControl.Api.Tests;

public sealed class RuntimeImageApiTests
{
    [Fact]
    public async Task Builder_catalog_returns_runtime_images()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var catalog = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog", ControlApiTestApplication.JsonOptions);

        Assert.NotNull(catalog);
        Assert.Equal(new[] { "elsa-pro-server", "elsa-pro-studio", "elsa-pro-combined" }.Order(), catalog!.Images.Select(x => x.Slug).Order());
        var combined = Assert.Single(catalog.Images, x => x.Slug == "elsa-pro-combined");
        Assert.Equal("elsaworkflows/elsa-pro-combined", combined.Image);
        Assert.Equal("latest", combined.DefaultTag);
        Assert.True(combined.DeploymentHints.SupportsDockerCompose);
        Assert.Contains(combined.EnvVars, x => x.Name == "ASPNETCORE_ENVIRONMENT");
    }
}
