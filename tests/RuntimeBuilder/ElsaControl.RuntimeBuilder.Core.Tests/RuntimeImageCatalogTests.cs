using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Core.Builder;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class RuntimeImageCatalogTests
{
    private const string AppSettingsJson = """
    {
      "RuntimeBuilder": {
        "Images": [
          {
            "Slug": "custom-runtime",
            "DisplayName": "Custom Runtime",
            "Description": "Runtime defined entirely by configuration.",
            "Image": "contoso/custom-runtime",
            "AvailableTags": [ "1.0", "latest" ],
            "DefaultTag": "latest",
            "DefaultPort": 8080,
            "HostPort": 9090,
            "ContainerName": "custom-runtime",
            "LicenseTier": "Professional",
            "Stability": "Stable",
            "Capabilities": [ "server" ],
            "RuntimeKinds": [ "elsa.server" ],
            "EnvVars": [
              {
                "Name": "ASPNETCORE_ENVIRONMENT",
                "DisplayName": "Environment",
                "Description": "ASP.NET Core environment.",
                "Required": false,
                "Secret": false,
                "DefaultValue": "Development",
                "Group": "Runtime",
                "Advanced": false
              }
            ],
            "DeploymentHints": {
              "SupportsDockerCompose": true,
              "SupportsKubernetes": true,
              "RequiresCompanionServer": false,
              "NeedsSharedNetwork": false,
              "CompanionImageSlug": null
            },
            "Docs": {
              "DockerHubUrl": "https://hub.docker.com/",
              "ContainerPaths": [ "/app" ],
              "ShowPerShellAdmin": false,
              "ShowNuplane": true
            }
          }
        ]
      }
    }
    """;

    [Fact]
    public void Catalog_is_defined_by_configuration()
    {
        var image = Assert.Single(CatalogFrom(AppSettingsJson).ListImages());

        Assert.Equal("custom-runtime", image.Slug);
        Assert.Equal("contoso/custom-runtime", image.Image);
        Assert.Equal("latest", image.DefaultTag);
        Assert.Equal(9090, image.HostPort);
        Assert.Equivalent(new[] { "1.0", "latest" }, image.AvailableTags);
        Assert.Equivalent(new[] { "elsa.server" }, image.RuntimeKinds);
        Assert.Equal("ASPNETCORE_ENVIRONMENT", Assert.Single(image.EnvVars).Name);
        Assert.True(image.DeploymentHints.SupportsDockerCompose);
        Assert.Equivalent(new[] { "/app" }, image.Docs.ContainerPaths);
    }

    [Fact]
    public void Catalog_is_empty_when_configuration_defines_no_images()
    {
        var catalog = CatalogFrom("""{ "RuntimeBuilder": { "Images": [] } }""");

        Assert.Empty(catalog.ListImages());
        Assert.Contains(new RuntimeImageValidator().Validate(catalog.ListImages()), x => x.Code == "runtimeImage.emptyCatalog");
    }

    [Fact]
    public void Catalog_is_empty_when_the_section_is_missing_entirely()
    {
        Assert.Empty(CatalogFrom("""{ "Database": { "Provider": "Sqlite" } }""").ListImages());
    }

    [Fact]
    public void Find_matches_a_slug_regardless_of_casing()
    {
        var catalog = RuntimeImageFixtures.Catalog();

        Assert.Equal("elsa-pro-combined", catalog.Find("ELSA-PRO-COMBINED")?.Slug);
        Assert.Null(catalog.Find("not-a-runtime"));
    }

    [Fact]
    public void Catalog_separates_deployment_metadata_from_docs_metadata()
    {
        var image = RuntimeImageFixtures.Catalog().Find("elsa-pro-combined");

        Assert.NotNull(image);
        Assert.Equal("elsaworkflows/elsa-pro-combined", image!.Image);
        Assert.Equal(8080, image.DefaultPort);
        Assert.NotEmpty(image.EnvVars);
        Assert.False(string.IsNullOrWhiteSpace(image.Docs.DockerHubUrl));
    }

    [Fact]
    public void Validation_accepts_a_well_formed_catalog()
    {
        Assert.Empty(new RuntimeImageValidator().Validate(RuntimeImageFixtures.Catalog().ListImages()));
    }

    [Fact]
    public void Validation_rejects_duplicate_slugs_and_missing_image_references()
    {
        var valid = RuntimeImageFixtures.Combined;
        var invalid = valid with { Image = "" };

        var findings = new RuntimeImageValidator().Validate([valid, invalid]);

        Assert.Contains(findings, x => x.Code == "runtimeImage.duplicateSlug");
        Assert.Contains(findings, x => x.Code == "runtimeImage.missingImage");
    }

    [Fact]
    public void Validation_rejects_invalid_default_tags_duplicate_env_vars_and_broken_companions()
    {
        var valid = RuntimeImageFixtures.Combined;
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
        var valid = RuntimeImageFixtures.Combined;
        var invalid = valid with
        {
            Slug = "custom",
            RuntimeKinds = ["elsa.server", "ELSA.SERVER", " "]
        };

        var findings = new RuntimeImageValidator().Validate([valid, invalid]);

        Assert.Contains(findings, x => x.Code == "runtimeImage.blankRuntimeKind");
        Assert.Contains(findings, x => x.Code == "runtimeImage.duplicateRuntimeKind");
    }

    private static RuntimeImageCatalog CatalogFrom(string json)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
        var options = configuration.GetSection(RuntimeBuilderOptions.SectionName).Get<RuntimeBuilderOptions>()
            ?? new RuntimeBuilderOptions();

        return new RuntimeImageCatalog(Options.Create(options));
    }
}
