using Elsa.Catalog.Core.Builder;
using FluentAssertions;

namespace Elsa.Catalog.Core.Tests;

public sealed class RuntimeImageCatalogTests
{
    [Fact]
    public void Catalog_contains_initial_runtime_images()
    {
        var images = new RuntimeImageCatalog().ListImages();

        images.Select(x => x.Slug).Should().BeEquivalentTo("elsa-pro-server", "elsa-pro-studio", "elsa-pro-combined");
        images.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Image));
        images.Should().OnlyContain(x => x.DeploymentHints.SupportsDockerCompose);
    }

    [Fact]
    public void Catalog_separates_deployment_metadata_from_docs_metadata()
    {
        var image = new RuntimeImageCatalog().Find("elsa-pro-combined");

        image.Should().NotBeNull();
        image!.Image.Should().Be("elsaworkflows/elsa-pro-combined");
        image.DefaultPort.Should().Be(8080);
        image.EnvVars.Should().NotBeEmpty();
        image.Docs.DockerHubUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validation_accepts_seeded_catalog()
    {
        var findings = new RuntimeImageValidator().Validate(new RuntimeImageCatalog().ListImages());

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Validation_rejects_duplicate_slugs_and_missing_image_references()
    {
        var valid = new RuntimeImageCatalog().Find("elsa-pro-combined")!;
        var invalid = valid with { Image = "" };

        var findings = new RuntimeImageValidator().Validate([valid, invalid]);

        findings.Should().Contain(x => x.Code == "runtimeImage.duplicateSlug");
        findings.Should().Contain(x => x.Code == "runtimeImage.missingImage");
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

        findings.Should().Contain(x => x.Code == "runtimeImage.invalidDefaultTag");
        findings.Should().Contain(x => x.Code == "runtimeImage.duplicateEnvVar");
        findings.Should().Contain(x => x.Code == "runtimeImage.brokenCompanion");
    }
}
