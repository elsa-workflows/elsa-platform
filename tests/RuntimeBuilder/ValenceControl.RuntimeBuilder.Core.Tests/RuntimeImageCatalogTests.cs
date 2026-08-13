using ValenceControl.RuntimeBuilder.Abstractions;
using ValenceControl.RuntimeBuilder.Core.Builder;

namespace ValenceControl.RuntimeBuilder.Core.Tests;

public sealed class RuntimeImageCatalogTests
{
    [Fact]
    public void Catalog_contains_initial_runtime_images()
    {
        var images = new RuntimeImageCatalog().ListImages();

        Assert.Equivalent(new[] { "elsa-pro-server", "elsa-pro-studio", "elsa-pro-combined" }, images.Select(x => x.Slug));
        Assert.All(images, x => Assert.False(string.IsNullOrWhiteSpace(x.Image)));
        Assert.All(images, x => Assert.True(x.DeploymentHints.SupportsDockerCompose));
        Assert.Equivalent(new[] { "elsa.server" }, images.Single(x => x.Slug == "elsa-pro-server").RuntimeKinds);
        Assert.Equivalent(new[] { "elsa.studio" }, images.Single(x => x.Slug == "elsa-pro-studio").RuntimeKinds);
        Assert.Equivalent(new[] { "elsa.server", "elsa.studio" }, images.Single(x => x.Slug == "elsa-pro-combined").RuntimeKinds);
    }

    [Fact]
    public void Catalog_separates_deployment_metadata_from_docs_metadata()
    {
        var image = new RuntimeImageCatalog().Find("elsa-pro-combined");

        Assert.NotNull(image);
        Assert.Equal("elsaworkflows/elsa-pro-combined", image!.Image);
        Assert.Equal(8080, image.DefaultPort);
        Assert.NotEmpty(image.EnvVars);
        Assert.False(string.IsNullOrWhiteSpace(image.Docs.DockerHubUrl));
    }

    [Fact]
    public void Validation_accepts_seeded_catalog()
    {
        var findings = new RuntimeImageValidator().Validate(new RuntimeImageCatalog().ListImages());

        Assert.Empty(findings);
    }

    [Fact]
    public void Validation_rejects_duplicate_slugs_and_missing_image_references()
    {
        var valid = new RuntimeImageCatalog().Find("elsa-pro-combined")!;
        var invalid = valid with { Image = "" };

        var findings = new RuntimeImageValidator().Validate([valid, invalid]);

        Assert.Contains(findings, x => x.Code == "runtimeImage.duplicateSlug");
        Assert.Contains(findings, x => x.Code == "runtimeImage.missingImage");
    }

    [Fact]
    public void Validation_rejects_invalid_default_tags_duplicate_env_vars_and_broken_companions()
    {
        var valid = new RuntimeImageCatalog().Find("elsa-pro-combined")!;
        var duplicateEnv = valid.EnvVars[0];
        var invalid = valid with
        {
            Slug = "custom",
            DefaultTag = "missing",
            EnvVars = [duplicateEnv, duplicateEnv],
            DeploymentHints = valid.DeploymentHints with
            {
                RequiresCompanionServer = true,
                CompanionImageSlug = "missing-companion"
            }
        };

        var findings = new RuntimeImageValidator().Validate([valid, invalid]);

        Assert.Contains(findings, x => x.Code == "runtimeImage.invalidDefaultTag");
        Assert.Contains(findings, x => x.Code == "runtimeImage.duplicateEnvVar");
        Assert.Contains(findings, x => x.Code == "runtimeImage.brokenCompanion");
    }

    [Fact]
    public void Validation_rejects_blank_and_duplicate_runtime_kinds()
    {
        var valid = new RuntimeImageCatalog().Find("elsa-pro-combined")!;
        var invalid = valid with
        {
            Slug = "custom",
            RuntimeKinds = ["elsa.server", "ELSA.SERVER", " "]
        };

        var findings = new RuntimeImageValidator().Validate([valid, invalid]);

        Assert.Contains(findings, x => x.Code == "runtimeImage.blankRuntimeKind");
        Assert.Contains(findings, x => x.Code == "runtimeImage.duplicateRuntimeKind");
    }
}
