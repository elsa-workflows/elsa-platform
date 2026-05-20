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

    [Theory]
    [InlineData("workflows")]
    [InlineData("variables")]
    [InlineData("features")]
    [InlineData("packages")]
    [InlineData("recipes")]
    public void NullResourceListsAreTreatedAsEmpty(string section)
    {
        var manifest = _reader.Read($"""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: null-resource-list
            resources:
              {section}: null
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize.Should().NotThrow().Subject;
        normalized.Diagnostics.Should().BeEmpty();
        normalized.Resources.Should().BeEmpty();
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

    [Fact]
    public void DependencyOrderDoesNotChangeDesiredStateHash()
    {
        var firstManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: dependency-order
            resources:
              workflows:
                - id: order-approval
                  path: workflows/order-approval.json
                  dependencies:
                    - type: variable
                      id: orderTimeout
                    - type: feature
                      id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var secondManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: dependency-order
            resources:
              workflows:
                - id: order-approval
                  path: workflows/order-approval.json
                  dependencies:
                    - type: feature
                      id: sales
                    - type: variable
                      id: orderTimeout
            """, ManifestFormat.Yaml).Manifest!;

        var first = _normalizer.Normalize(firstManifest);
        var second = _normalizer.Normalize(secondManifest);

        first.Diagnostics.Should().BeEmpty();
        second.Diagnostics.Should().BeEmpty();
        var firstResource = first.Resources.Should().ContainSingle().Which;
        var secondResource = second.Resources.Should().ContainSingle().Which;
        firstResource.DesiredStateHash.Should().Be(secondResource.DesiredStateHash);
    }

    [Fact]
    public void NullYamlAndJsonValuesProduceEquivalentHashes()
    {
        var yamlManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: null-value
            resources:
              variables:
                - key: optionalValue
                  value: null
            """, ManifestFormat.Yaml).Manifest!;
        var jsonManifest = _reader.Read("""
            {
              "apiVersion": "platform.elsa.io/v1alpha1",
              "kind": "EnvironmentManifest",
              "metadata": { "name": "null-value" },
              "resources": {
                "variables": [
                  { "key": "optionalValue", "value": null }
                ]
              }
            }
            """, ManifestFormat.Json).Manifest!;

        var yaml = _normalizer.Normalize(yamlManifest);
        var json = _normalizer.Normalize(jsonManifest);

        yaml.Diagnostics.Should().BeEmpty();
        json.Diagnostics.Should().BeEmpty();
        yaml.Resources.Should().ContainSingle().Which.DesiredStateHash
            .Should().Be(json.Resources.Should().ContainSingle().Which.DesiredStateHash);
    }

    [Fact]
    public void ScientificNotationYamlAndJsonValuesProduceEquivalentHashes()
    {
        var yamlManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: exponent-value
            resources:
              variables:
                - key: largeNumber
                  value: 1e5
            """, ManifestFormat.Yaml).Manifest!;
        var jsonManifest = _reader.Read("""
            {
              "apiVersion": "platform.elsa.io/v1alpha1",
              "kind": "EnvironmentManifest",
              "metadata": { "name": "exponent-value" },
              "resources": {
                "variables": [
                  { "key": "largeNumber", "value": 1e5 }
                ]
              }
            }
            """, ManifestFormat.Json).Manifest!;

        var yaml = _normalizer.Normalize(yamlManifest);
        var json = _normalizer.Normalize(jsonManifest);

        yaml.Diagnostics.Should().BeEmpty();
        json.Diagnostics.Should().BeEmpty();
        yaml.Resources.Should().ContainSingle().Which.DesiredStateHash
            .Should().Be(json.Resources.Should().ContainSingle().Which.DesiredStateHash);
    }

    [Fact]
    public void OutOfRangeScientificNotationYamlAndJsonValuesProduceEquivalentHashes()
    {
        var yamlManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: out-of-range-exponent-value
            resources:
              variables:
                - key: largeNumber
                  value: 1e400
            """, ManifestFormat.Yaml).Manifest!;
        var jsonManifest = _reader.Read("""
            {
              "apiVersion": "platform.elsa.io/v1alpha1",
              "kind": "EnvironmentManifest",
              "metadata": { "name": "out-of-range-exponent-value" },
              "resources": {
                "variables": [
                  { "key": "largeNumber", "value": 1e400 }
                ]
              }
            }
            """, ManifestFormat.Json).Manifest!;

        var yaml = _normalizer.Normalize(yamlManifest);
        var json = _normalizer.Normalize(jsonManifest);

        yaml.Diagnostics.Should().BeEmpty();
        json.Diagnostics.Should().BeEmpty();
        yaml.Resources.Should().ContainSingle().Which.DesiredStateHash
            .Should().Be(json.Resources.Should().ContainSingle().Which.DesiredStateHash);
    }

    [Fact]
    public void OutOfRangeNumericAndStringValuesProduceDifferentHashes()
    {
        var numericManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: out-of-range-exponent-value
            resources:
              variables:
                - key: largeNumber
                  value: 1e400
            """, ManifestFormat.Yaml).Manifest!;
        var stringManifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: out-of-range-exponent-value
            resources:
              variables:
                - key: largeNumber
                  value: "1e400"
            """, ManifestFormat.Yaml).Manifest!;

        var numeric = _normalizer.Normalize(numericManifest);
        var text = _normalizer.Normalize(stringManifest);

        numeric.Diagnostics.Should().BeEmpty();
        text.Diagnostics.Should().BeEmpty();
        numeric.Resources.Should().ContainSingle().Which.DesiredStateHash
            .Should().NotBe(text.Resources.Should().ContainSingle().Which.DesiredStateHash);
    }

    [Theory]
    [InlineData("workflows")]
    [InlineData("variables")]
    [InlineData("features")]
    [InlineData("packages")]
    [InlineData("recipes")]
    public void NullResourceMetadataIsTreatedAsEmpty(string section)
    {
        var resource = section switch
        {
            "workflows" => """
                  id: order-approval
                  path: workflows/order-approval.json
                  metadata: null
            """,
            "variables" => """
                  key: orderTimeout
                  metadata: null
            """,
            "features" => """
                  id: sales
                  metadata: null
            """,
            "packages" => """
                  id: Elsa.Workflows
                  version: 3.0.0
                  metadata: null
            """,
            "recipes" => """
                  id: initialize-sales
                  metadata: null
            """,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };
        var manifest = _reader.Read($"""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: null-resource-metadata
            resources:
              {section}:
                -
            {resource}
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize.Should().NotThrow().Subject;
        normalized.Diagnostics.Should().BeEmpty();
        normalized.Resources.Should().ContainSingle();
    }
}
