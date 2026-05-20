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
    }

    [Fact]
    public void PathEscapingManifestRootReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: invalid-path
            resources:
              workflows:
                - id: order-approval
                  path: ../order-approval.json
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.ResourcePathInvalid);
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
