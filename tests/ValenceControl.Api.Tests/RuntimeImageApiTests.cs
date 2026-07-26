using System.Net.Http.Json;
using ValenceControl.Api.Public.Builder;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

public sealed class RuntimeImageApiTests
{
    [Fact]
    public async Task Builder_catalog_returns_runtime_images()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var catalog = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog", ControlApiTestApplication.JsonOptions);

        catalog.Should().NotBeNull();
        catalog!.Images.Select(x => x.Slug).Should().BeEquivalentTo("elsa-pro-server", "elsa-pro-studio", "elsa-pro-combined");
        var combined = catalog.Images.Should().ContainSingle(x => x.Slug == "elsa-pro-combined").Subject;
        combined.Image.Should().Be("elsaworkflows/elsa-pro-combined");
        combined.DefaultTag.Should().Be("latest");
        combined.DeploymentHints.SupportsDockerCompose.Should().BeTrue();
        combined.EnvVars.Should().Contain(x => x.Name == "ASPNETCORE_ENVIRONMENT");
    }
}
