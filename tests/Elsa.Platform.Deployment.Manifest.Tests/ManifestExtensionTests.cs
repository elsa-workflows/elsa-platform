using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Manifest.Tests;

public class ManifestExtensionTests
{
    private readonly ManifestReader _reader = new();
    private readonly ManifestNormalizer _normalizer = new();

    [Fact]
    public void UnknownResourceSectionReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        normalized.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.ResourceUnsupported && x.Severity == DeploymentDiagnosticSeverity.Error);
    }

    [Fact]
    public void RegisteredResourceMapperNormalizesCustomSection()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var registry = new ManifestResourceMapperRegistry().Add(new DashboardMapper());

        var normalized = _normalizer.Normalize(manifest, registry);

        normalized.Diagnostics.Should().BeEmpty();
        normalized.Resources.Should().ContainSingle().Which.Id.Should().Be(new DeploymentResourceId("dashboard", "sales"));
    }

    [Fact]
    public void ResourceMapperDiagnosticsAreAppendOnly()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: ''
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var mapper = new DiagnosticMapper();
        var registry = new ManifestResourceMapperRegistry().Add(mapper);

        var normalized = _normalizer.Normalize(manifest, registry);

        mapper.DiagnosticsWereMutable.Should().BeFalse();
        normalized.Diagnostics.Select(x => x.Code).Should().Contain([
            ManifestDiagnosticCodes.MetadataNameRequired,
            "dashboard.invalid"
        ]);
    }

    [Fact]
    public void ManifestAndResourceMetadataArePreserved()
    {
        var manifest = _reader.Read("""
            apiVersion: platform.elsa.io/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: sales-staging
              version: 2026.05.20.1
              environment: staging
              labels:
                team: sales
                tier: customer
              annotations:
                sourceCommit: abc123
                deploymentReason: smoke-test
            resources:
              workflows:
                - id: order-approval
                  path: workflows/order-approval.json
                  metadata:
                    owner: approvals
                    runtime: serverless
              variables:
                - key: orderTimeout
                  scope: sales
                  value: 30
                  metadata:
                    unit: seconds
            """, ManifestFormat.Yaml).Manifest!;

        var normalized = _normalizer.Normalize(manifest);

        manifest.Metadata.Labels.Should().Contain("team", "sales").And.Contain("tier", "customer");
        manifest.Metadata.Annotations.Should().Contain("sourceCommit", "abc123").And.Contain("deploymentReason", "smoke-test");
        normalized.Resources.Should().Contain(resource =>
            resource.Id == new DeploymentResourceId(DeploymentManifestConstants.WorkflowDefinitionResourceType, "order-approval") &&
            resource.Metadata.Contains(new KeyValuePair<string, string>("owner", "approvals")) &&
            resource.Metadata.Contains(new KeyValuePair<string, string>("runtime", "serverless")));
        normalized.Resources.Should().Contain(resource =>
            resource.Id == new DeploymentResourceId(DeploymentManifestConstants.VariableResourceType, "orderTimeout", "sales") &&
            resource.Metadata.Contains(new KeyValuePair<string, string>("unit", "seconds")));
    }

    private sealed class DashboardMapper : IManifestResourceMapper
    {
        public string SectionName => "dashboards";

        public IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context) =>
        [
            new DeploymentResource(
                new DeploymentResourceId("dashboard", "sales"),
                desiredStateHash: new ArtifactDigest("sha256", "custom"))
        ];
    }

    private sealed class DiagnosticMapper : IManifestResourceMapper
    {
        public bool DiagnosticsWereMutable { get; private set; }

        public string SectionName => "dashboards";

        public IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context)
        {
            DiagnosticsWereMutable = context.Diagnostics is IList<DeploymentDiagnostic>;
            context.AddDiagnostic(new DeploymentDiagnostic(
                "dashboard.invalid",
                DeploymentDiagnosticSeverity.Error,
                "Dashboard resource is invalid."));
            return [];
        }
    }
}
