using System.Globalization;
using ValenceControl.Deployment.Abstractions.Diagnostics;
using FluentAssertions;

namespace ValenceControl.Deployment.Manifest.Tests;

public class ManifestReaderTests
{
    private readonly ManifestReader _reader = new();

    [Fact]
    public void ReadsValidYamlManifest()
    {
        var result = _reader.Read(SampleYaml, ManifestFormat.Yaml);

        result.Diagnostics.Should().BeEmpty();
        result.Manifest.Should().NotBeNull();
        result.Manifest!.ApiVersion.Should().Be(DeploymentManifestConstants.ApiVersion);
        result.Manifest.Kind.Should().Be(DeploymentManifestConstants.Kind);
        result.Manifest.Metadata.Name.Should().Be("sales-staging");
        result.Manifest.Resources.Workflows.Should().ContainSingle().Which.Id.Should().Be("order-approval");
        result.Manifest.Resources.Variables.Should().ContainSingle().Which.Key.Should().Be("orderTimeout");
    }

    [Fact]
    public void ReadsValidJsonManifest()
    {
        var result = _reader.Read(SampleJson, ManifestFormat.Json);

        result.Diagnostics.Should().BeEmpty();
        result.Manifest.Should().NotBeNull();
        result.Manifest!.Metadata.Name.Should().Be("sales-staging");
        result.Manifest.Resources.Packages.Should().ContainSingle().Which.Id.Should().Be("Acme.Sales");
    }

    [Fact]
    public void MalformedYamlReturnsParseDiagnostic()
    {
        var result = _reader.Read("apiVersion: [", ManifestFormat.Yaml);

        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.Parse && x.Severity == DeploymentDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(ManifestFormat.Yaml, "resources: null")]
    [InlineData(ManifestFormat.Json, """{ "resources": null }""")]
    public void NullResourcesReturnsParseDiagnostic(ManifestFormat format, string text)
    {
        var read = () => _reader.Read(text, format);

        var result = read.Should().NotThrow().Subject;
        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.Parse &&
            x.Message == "Manifest 'resources' must be an object, not null.");
    }

    [Fact]
    public void MultipleYamlDocumentsReturnParseDiagnostic()
    {
        var result = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-staging
            resources: {}
            ---
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-production
            resources: {}
            """, ManifestFormat.Yaml);

        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.Parse &&
            x.Message == "Manifest YAML must contain exactly one document.");
    }

    [Theory]
    [InlineData(ManifestFormat.Yaml, """
        apiVersion: valence-control/v1alpha1
        kind: EnvironmentManifest
        metadata:
          name: sales-staging
          labels: null
          annotations: null
        resources: {}
        """)]
    [InlineData(ManifestFormat.Json, """
        {
          "apiVersion": "valence-control/v1alpha1",
          "kind": "EnvironmentManifest",
          "metadata": {
            "name": "sales-staging",
            "labels": null,
            "annotations": null
          },
          "resources": {}
        }
        """)]
    public void ExplicitNullMetadataDictionariesAreTreatedAsEmpty(ManifestFormat format, string text)
    {
        var result = _reader.Read(text, format);

        result.Diagnostics.Should().BeEmpty();
        result.Manifest!.Metadata.Labels.Should().BeEmpty();
        result.Manifest.Metadata.Annotations.Should().BeEmpty();
    }

    [Fact]
    public void JsonReaderPreservesExplicitStringVariableValues()
    {
        var result = _reader.Read("""
            {
              "apiVersion": "valence-control/v1alpha1",
              "kind": "EnvironmentManifest",
              "metadata": { "name": "sales-staging" },
              "resources": {
                "variables": [
                  { "key": "code", "value": "0001" },
                  { "key": "flagText", "value": "true" }
                ]
              }
            }
            """, ManifestFormat.Json);

        result.Manifest!.Resources.Variables[0].Value!.ToJsonString().Should().Be("\"0001\"");
        result.Manifest.Resources.Variables[1].Value!.ToJsonString().Should().Be("\"true\"");
    }

    [Fact]
    public void YamlReaderPreservesQuotedStringVariableValues()
    {
        var result = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-staging
            resources:
              variables:
                - key: code
                  value: "0001"
                - key: flagText
                  value: "true"
            """, ManifestFormat.Yaml);

        result.Manifest!.Resources.Variables[0].Value!.ToJsonString().Should().Be("\"0001\"");
        result.Manifest.Resources.Variables[1].Value!.ToJsonString().Should().Be("\"true\"");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("Null")]
    [InlineData("NULL")]
    [InlineData("~")]
    public void YamlReaderConvertsPlainNullScalarsToJsonNull(string value)
    {
        var result = _reader.Read($"""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-staging
            resources:
              variables:
                - key: optionalValue
                  value: {value}
            """, ManifestFormat.Yaml);

        result.Manifest!.Resources.Variables[0].Value.Should().BeNull();
    }

    [Fact]
    public void YamlReaderConvertsNumericScalarsUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nl-NL");

            var result = _reader.Read("""
                apiVersion: valence-control/v1alpha1
                kind: EnvironmentManifest
                metadata:
                  name: sales-staging
                resources:
                  variables:
                    - key: ratio
                      value: 1.5
                """, ManifestFormat.Yaml);

            result.Diagnostics.Should().BeEmpty();
            result.Manifest!.Resources.Variables[0].Value!.ToJsonString().Should().Be("1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void YamlReaderPreservesThousandsSeparatedPlainScalarsAsStrings()
    {
        var result = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-staging
            resources:
              variables:
                - key: displayValue
                  value: 1,000
            """, ManifestFormat.Yaml);

        result.Manifest!.Resources.Variables[0].Value!.ToJsonString().Should().Be("\"1,000\"");
    }

    [Fact]
    public void YamlReaderConvertsScientificNotationScalarsToNumbers()
    {
        var result = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-staging
            resources:
              variables:
                - key: largeNumber
                  value: 1e5
            """, ManifestFormat.Yaml);

        result.Manifest!.Resources.Variables[0].Value!.ToJsonString().Should().Be("100000");
    }

    [Fact]
    public void ReaderDoesNotDuplicateHeaderValidationDiagnosticsOwnedByNormalizer()
    {
        var result = _reader.Read("""
            apiVersion: valence-control/v2
            kind: WorkflowManifest
            metadata: {}
            """, ManifestFormat.Yaml);

        result.Diagnostics.Should().BeEmpty();
        result.Manifest.Should().NotBeNull();
    }

    public const string SampleYaml = """
        apiVersion: valence-control/v1alpha1
        kind: EnvironmentManifest
        metadata:
          name: sales-staging
          version: 2026.05.20.1
          environment: staging
          labels:
            team: sales
        resources:
          workflows:
            - id: order-approval
              path: workflows/order-approval.json
              activation: active
          variables:
            - key: orderTimeout
              value: 30
              scope: sales
          features:
            - id: sales
              state: enabled
          packages:
            - id: Acme.Sales
              version: 1.4.2
          recipes:
            - id: initialize-sales
              path: recipes/initialize-sales.yaml
        """;

    public const string SampleJson = """
        {
          "apiVersion": "valence-control/v1alpha1",
          "kind": "EnvironmentManifest",
          "metadata": {
            "name": "sales-staging",
            "version": "2026.05.20.1",
            "environment": "staging"
          },
          "resources": {
            "workflows": [{ "id": "order-approval", "path": "workflows/order-approval.json", "activation": "active" }],
            "variables": [{ "key": "orderTimeout", "value": 30, "scope": "sales" }],
            "features": [{ "id": "sales", "state": "enabled" }],
            "packages": [{ "id": "Acme.Sales", "version": "1.4.2" }],
            "recipes": [{ "id": "initialize-sales", "path": "recipes/initialize-sales.yaml" }]
          }
        }
        """;
}
