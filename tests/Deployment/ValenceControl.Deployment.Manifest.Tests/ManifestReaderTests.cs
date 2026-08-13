using System.Globalization;
using ValenceControl.Deployment.Abstractions.Diagnostics;

namespace ValenceControl.Deployment.Manifest.Tests;

public class ManifestReaderTests
{
    private readonly ManifestReader _reader = new();

    [Fact]
    public void ReadsValidYamlManifest()
    {
        var result = _reader.Read(SampleYaml, ManifestFormat.Yaml);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Manifest);
        Assert.Equal(DeploymentManifestConstants.ApiVersion, result.Manifest!.ApiVersion);
        Assert.Equal(DeploymentManifestConstants.Kind, result.Manifest.Kind);
        Assert.Equal("sales-staging", result.Manifest.Metadata.Name);
        Assert.Equal("order-approval", Assert.Single(result.Manifest.Resources.Workflows).Id);
        Assert.Equal("orderTimeout", Assert.Single(result.Manifest.Resources.Variables).Key);
    }

    [Fact]
    public void ReadsValidJsonManifest()
    {
        var result = _reader.Read(SampleJson, ManifestFormat.Json);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Manifest);
        Assert.Equal("sales-staging", result.Manifest!.Metadata.Name);
        Assert.Equal("Acme.Sales", Assert.Single(result.Manifest.Resources.Packages).Id);
    }

    [Fact]
    public void MalformedYamlReturnsParseDiagnostic()
    {
        var result = _reader.Read("apiVersion: [", ManifestFormat.Yaml);

        Assert.Null(result.Manifest);
        Assert.Single(result.Diagnostics, x =>
            x.Code == ManifestDiagnosticCodes.Parse && x.Severity == DeploymentDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(ManifestFormat.Yaml, "resources: null")]
    [InlineData(ManifestFormat.Json, """{ "resources": null }""")]
    public void NullResourcesReturnsParseDiagnostic(ManifestFormat format, string text)
    {
        var read = () => _reader.Read(text, format);

        var result = read();
        Assert.Null(result.Manifest);
        Assert.Single(result.Diagnostics, x =>
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

        Assert.Null(result.Manifest);
        Assert.Single(result.Diagnostics, x =>
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

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Manifest!.Metadata.Labels);
        Assert.Empty(result.Manifest.Metadata.Annotations);
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

        Assert.Equal("\"0001\"", result.Manifest!.Resources.Variables[0].Value!.ToJsonString());
        Assert.Equal("\"true\"", result.Manifest.Resources.Variables[1].Value!.ToJsonString());
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

        Assert.Equal("\"0001\"", result.Manifest!.Resources.Variables[0].Value!.ToJsonString());
        Assert.Equal("\"true\"", result.Manifest.Resources.Variables[1].Value!.ToJsonString());
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

        Assert.Null(result.Manifest!.Resources.Variables[0].Value);
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

            Assert.Empty(result.Diagnostics);
            Assert.Equal("1.5", result.Manifest!.Resources.Variables[0].Value!.ToJsonString());
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

        Assert.Equal("\"1,000\"", result.Manifest!.Resources.Variables[0].Value!.ToJsonString());
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

        Assert.Equal("100000", result.Manifest!.Resources.Variables[0].Value!.ToJsonString());
    }

    [Fact]
    public void ReaderDoesNotDuplicateHeaderValidationDiagnosticsOwnedByNormalizer()
    {
        var result = _reader.Read("""
            apiVersion: valence-control/v2
            kind: WorkflowManifest
            metadata: {}
            """, ManifestFormat.Yaml);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Manifest);
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
