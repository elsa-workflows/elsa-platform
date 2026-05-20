using FluentAssertions;

namespace Elsa.Platform.Deployment.Manifest.Tests;

public class ManifestNormalizationTests
{
    private readonly ManifestReader _reader = new();
    private readonly ManifestNormalizer _normalizer = new();

    [Fact]
    public void NormalizesBuiltInResources()
    {
        var manifest = _reader.Read(ManifestReaderTests.SampleYaml, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        normalized.Diagnostics.Should().BeEmpty();
        normalized.Resources.Select(x => x.Id.Type).Should().Equal(
            DeploymentManifestConstants.WorkflowDefinitionResourceType,
            DeploymentManifestConstants.VariableResourceType,
            DeploymentManifestConstants.FeatureResourceType,
            DeploymentManifestConstants.PackageResourceType,
            DeploymentManifestConstants.RecipeResourceType);
        normalized.Resources.Should().OnlyContain(x => x.DesiredStateHash != null);
    }

    [Fact]
    public void EquivalentYamlAndJsonProduceEquivalentHashes()
    {
        var yamlManifest = _reader.Read(ManifestReaderTests.SampleYaml, ManifestFormat.Yaml).Manifest!;
        var jsonManifest = _reader.Read(ManifestReaderTests.SampleJson, ManifestFormat.Json).Manifest!;

        var yaml = _normalizer.Normalize(yamlManifest);
        var json = _normalizer.Normalize(jsonManifest);

        yaml.Resources.Select(x => x.DesiredStateHash).Should().Equal(json.Resources.Select(x => x.DesiredStateHash));
    }
}
