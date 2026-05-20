using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Manifest.Tests;

public class ManifestDiagnosticTests
{
    private readonly ManifestReader _reader = new();
    private readonly ManifestNormalizer _normalizer = new();

    [Fact]
    public void UnsupportedApiVersionReturnsDiagnostic()
    {
        var manifest = _reader.Read(ManifestReaderTests.SampleYaml.Replace(DeploymentManifestConstants.ApiVersion, "platform.elsa.io/v2"), ManifestFormat.Yaml).Manifest!;

        var result = _normalizer.Normalize(manifest);

        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.ApiVersionUnsupported && x.Severity == DeploymentDiagnosticSeverity.Error);
    }

    [Fact]
    public void UnsupportedKindReturnsDiagnostic()
    {
        var manifest = _reader.Read(ManifestReaderTests.SampleYaml.Replace(DeploymentManifestConstants.Kind, "WorkflowManifest"), ManifestFormat.Yaml).Manifest!;

        var result = _normalizer.Normalize(manifest);

        result.Diagnostics.Should().ContainSingle(x =>
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

        normalized.Diagnostics.Select(x => x.Code).Should().Contain([
            ManifestDiagnosticCodes.ApiVersionRequired,
            ManifestDiagnosticCodes.KindRequired,
            ManifestDiagnosticCodes.MetadataNameRequired,
            ManifestDiagnosticCodes.ResourceIdentityRequired
        ]);
    }

    [Fact]
    public void NullMetadataReturnsNameRequiredDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata: null
            resources: {}
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize.Should().NotThrow().Subject;
        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.MetadataNameRequired);
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
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: null-entry
            resources:
              {section}:
                - null
            """, ManifestFormat.Yaml).Manifest!;

        var normalize = () => _normalizer.Normalize(manifest);

        var normalized = normalize.Should().NotThrow().Subject;
        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.ResourceIdentityRequired);
        normalized.Resources.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateResourceIdentityReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: duplicate
            resources:
              variables:
                - key: orderTimeout
                - key: orderTimeout
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.ResourceDuplicate);
        normalized.Resources.Should().ContainSingle();
    }

    [Theory]
    [InlineData("type: variable")]
    [InlineData("id: orderTimeout")]
    public void IncompleteDependencyReturnsDiagnostic(string dependency)
    {
        var manifest = _reader.Read($"""
            apiVersion: platform.elsa.io/v1alpha1
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

        normalized.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.ResourceDependencyInvalid &&
            x.ResourceId.HasValue &&
            x.ResourceId.Value.Type == DeploymentManifestConstants.WorkflowDefinitionResourceType &&
            x.ResourceId.Value.LogicalId == "order-approval");
        normalized.Resources.Should().ContainSingle().Which.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void NullDependencyEntryReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
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

        var normalized = normalize.Should().NotThrow().Subject;
        normalized.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.ResourceDependencyInvalid &&
            x.ResourceId.HasValue &&
            x.ResourceId.Value.Type == DeploymentManifestConstants.WorkflowDefinitionResourceType &&
            x.ResourceId.Value.LogicalId == "order-approval");
        normalized.Resources.Should().ContainSingle().Which.Dependencies.Should().BeEmpty();
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
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: invalid-path
            resources:
              {section}:
                - {identity}
                  path: {path}
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.ResourcePathInvalid);
        normalized.Resources.Should().BeEmpty();
    }

    [Fact]
    public void MissingWorkflowPathReturnsPathRequiredDiagnosticAndSkipsResource()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: missing-path
            resources:
              workflows:
                - id: order-approval
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.ResourcePathRequired);
        normalized.Resources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ManifestFormat.Yaml, "apiVersion: [")]
    [InlineData(ManifestFormat.Json, """{ "apiVersion": "platform.elsa.io/v1alpha1", "kind": """)]
    public void MalformedYamlOrJsonReturnsParseDiagnostic(ManifestFormat format, string text)
    {
        var read = () => _reader.Read(text, format);

        var result = read.Should().NotThrow().Subject;
        result.Manifest.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.Parse && x.Severity == DeploymentDiagnosticSeverity.Error);
    }
}
