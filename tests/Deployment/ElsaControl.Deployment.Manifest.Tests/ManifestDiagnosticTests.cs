using ElsaControl.Deployment.Abstractions.Diagnostics;

namespace ElsaControl.Deployment.Manifest.Tests;

public class ManifestDiagnosticTests
{
    private readonly ManifestReader _reader = new();
    private readonly ManifestNormalizer _normalizer = new();

    [Fact]
    public void UnsupportedApiVersionReturnsDiagnostic()
    {
        var manifest = _reader.Read(ManifestReaderTests.SampleYaml.Replace(DeploymentManifestConstants.ApiVersion, "elsa-control/v2"), ManifestFormat.Yaml).Manifest!;

        var result = _normalizer.Normalize(manifest);

        Assert.Single(result.Diagnostics, x =>
            x.Code == ManifestDiagnosticCodes.ApiVersionUnsupported && x.Severity == DeploymentDiagnosticSeverity.Error);
    }

    [Fact]
    public void UnsupportedKindReturnsDiagnostic()
    {
        var manifest = _reader.Read(ManifestReaderTests.SampleYaml.Replace(DeploymentManifestConstants.Kind, "WorkflowManifest"), ManifestFormat.Yaml).Manifest!;

        var result = _normalizer.Normalize(manifest);

        Assert.Single(result.Diagnostics, x =>
            x.Code == ManifestDiagnosticCodes.KindUnsupported && x.Severity == DeploymentDiagnosticSeverity.Error);
    }

    [Fact]
    public void MissingRequiredFieldsReturnStableDiagnostics()
    {
        var manifest = _reader.Read("""
            resources:
              workflows:
                - path: workflows/order-approval.json
              variables:
                - value: 30
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        var diagnosticCodes = normalized.Diagnostics.Select(x => x.Code);
        Assert.Contains(ManifestDiagnosticCodes.ApiVersionRequired, diagnosticCodes);
        Assert.Contains(ManifestDiagnosticCodes.KindRequired, diagnosticCodes);
        Assert.Contains(ManifestDiagnosticCodes.MetadataNameRequired, diagnosticCodes);
        Assert.Contains(ManifestDiagnosticCodes.ResourceIdentityRequired, diagnosticCodes);
    }

    [Fact]
    public void NullMetadataReturnsNameRequiredDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata: null
            resources: {}
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize();
        Assert.Single(normalized.Diagnostics, x => x.Code == ManifestDiagnosticCodes.MetadataNameRequired);
    }

    [Theory]
    [InlineData("workflows")]
    [InlineData("variables")]
    [InlineData("features")]
    [InlineData("packages")]
    [InlineData("recipes")]
    public void NullResourceListEntryReturnsIdentityDiagnostic(string section)
    {
        var manifest = _reader.Read($"""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: null-entry
            resources:
              {section}:
                - null
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize();
        Assert.Single(normalized.Diagnostics, x => x.Code == ManifestDiagnosticCodes.ResourceIdentityRequired);
        Assert.Empty(normalized.Resources);
    }

    [Fact]
    public void DuplicateResourceIdentityReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: duplicate
            resources:
              variables:
                - key: orderTimeout
                - key: orderTimeout
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        Assert.Single(normalized.Diagnostics, x => x.Code == ManifestDiagnosticCodes.ResourceDuplicate);
        Assert.Single(normalized.Resources);
    }

    [Theory]
    [InlineData("type: variable")]
    [InlineData("id: orderTimeout")]
    public void IncompleteDependencyReturnsDiagnostic(string dependency)
    {
        var manifest = _reader.Read($"""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: invalid-dependency
            resources:
              workflows:
                - id: order-approval
                  path: workflows/order-approval.json
                  dependencies:
                    - {dependency}
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        Assert.Single(normalized.Diagnostics, x =>
            x.Code == ManifestDiagnosticCodes.ResourceDependencyInvalid &&
            x.ResourceId.HasValue &&
            x.ResourceId.Value.Type == DeploymentManifestConstants.WorkflowDefinitionResourceType &&
            x.ResourceId.Value.LogicalId == "order-approval");
        Assert.Empty(Assert.Single(normalized.Resources).Dependencies);
    }

    [Fact]
    public void NullDependencyEntryReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: null-dependency
            resources:
              workflows:
                - id: order-approval
                  path: workflows/order-approval.json
                  dependencies:
                    - null
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize();
        Assert.Single(normalized.Diagnostics, x =>
            x.Code == ManifestDiagnosticCodes.ResourceDependencyInvalid &&
            x.ResourceId.HasValue &&
            x.ResourceId.Value.Type == DeploymentManifestConstants.WorkflowDefinitionResourceType &&
            x.ResourceId.Value.LogicalId == "order-approval");
        Assert.Empty(Assert.Single(normalized.Resources).Dependencies);
    }

    [Theory]
    [InlineData("workflows", "id: order-approval", "../order-approval.json")]
    [InlineData("recipes", "id: initialize-sales", "../initialize-sales.yaml")]
    [InlineData("workflows", "id: order-approval", "./order-approval.json")]
    [InlineData("recipes", "id: initialize-sales", "recipes/./initialize-sales.yaml")]
    [InlineData("workflows", "id: order-approval", "C:\\workflows\\order-approval.json")]
    [InlineData("recipes", "id: initialize-sales", "/recipes/initialize-sales.yaml")]
    public void InvalidPathReturnsDiagnosticAndSkipsResource(string section, string identity, string path)
    {
        var manifest = _reader.Read($"""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: invalid-path
            resources:
              {section}:
                - {identity}
                  path: {path}
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        Assert.Single(normalized.Diagnostics, x => x.Code == ManifestDiagnosticCodes.ResourcePathInvalid);
        Assert.Empty(normalized.Resources);
    }

    [Fact]
    public void MissingWorkflowPathReturnsPathRequiredDiagnosticAndSkipsResource()
    {
        var manifest = _reader.Read("""
            apiVersion: elsa-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: missing-path
            resources:
              workflows:
                - id: order-approval
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        Assert.Single(normalized.Diagnostics, x => x.Code == ManifestDiagnosticCodes.ResourcePathRequired);
        Assert.Empty(normalized.Resources);
    }

    [Theory]
    [InlineData(ManifestFormat.Yaml, "apiVersion: [")]
    [InlineData(ManifestFormat.Json, """{ "apiVersion": "elsa-control/v1alpha1", "kind": """)]
    public void MalformedYamlOrJsonReturnsParseDiagnostic(ManifestFormat format, string text)
    {
        var read = () => _reader.Read(text, format);

        var result = read();
        Assert.Null(result.Manifest);
        Assert.Single(result.Diagnostics, x =>
            x.Code == ManifestDiagnosticCodes.Parse && x.Severity == DeploymentDiagnosticSeverity.Error);
    }
}
