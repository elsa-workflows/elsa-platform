using Microsoft.Extensions.DependencyInjection;
using ElsaControl.RuntimeBuilder.Core.Builder;

namespace ElsaControl.Api.Tests;

public sealed class RuntimeImageCatalogConfigurationTests
{
    [Fact]
    public async Task Shipped_configuration_defines_the_runtime_image_catalog()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var images = app.Services.GetRequiredService<RuntimeImageCatalog>().ListImages();

        Assert.Equivalent(new[] { "elsa-pro-server", "elsa-pro-studio", "elsa-pro-combined" }, images.Select(x => x.Slug));
        Assert.Empty(new RuntimeImageValidator().Validate(images));
        Assert.All(images, x => Assert.False(string.IsNullOrWhiteSpace(x.Image)));
        Assert.Equivalent(new[] { "elsa.server" }, images.Single(x => x.Slug == "elsa-pro-server").RuntimeKinds);
        Assert.Equivalent(new[] { "elsa.studio" }, images.Single(x => x.Slug == "elsa-pro-studio").RuntimeKinds);
        Assert.Equivalent(new[] { "elsa.server", "elsa.studio" }, images.Single(x => x.Slug == "elsa-pro-combined").RuntimeKinds);
        Assert.Equal("elsa-pro-server", images.Single(x => x.Slug == "elsa-pro-studio").DeploymentHints.CompanionImageSlug);
    }
}
