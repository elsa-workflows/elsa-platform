using System.Text.Json.Nodes;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace ValenceControl.Deployment.Manifest.Tests;

public class ManifestExtensionTests
{
    private readonly ManifestReader _reader = new();
    private readonly ManifestNormalizer _normalizer = new();

    [Fact]
    public void UnknownResourceSectionReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
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
            apiVersion: valence-control/v1alpha1
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
    public void ExtensionSectionsAreDiscoveredWithCaseInsensitiveResourcesKey()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            Resources:
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
            apiVersion: valence-control/v1alpha1
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
    public void CapturedResourceMapperContextCannotMutateNormalizedDiagnostics()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var mapper = new CapturingMapper();
        var registry = new ManifestResourceMapperRegistry().Add(mapper);

        var normalized = _normalizer.Normalize(manifest, registry);
        mapper.Context!.AddDiagnostic(new DeploymentDiagnostic(
            "dashboard.late",
            DeploymentDiagnosticSeverity.Error,
            "Late dashboard diagnostic."));

        normalized.Diagnostics.Should().BeEmpty();
        mapper.Context.Diagnostics.Should().Contain(x => x.Code == "dashboard.late");
    }

    [Fact]
    public void DuplicateResourceMapperRegistrationThrows()
    {
        var registry = new ManifestResourceMapperRegistry().Add(new DashboardMapper());

        var addDuplicate = () => registry.Add(new DashboardMapper());

        addDuplicate.Should().Throw<InvalidOperationException>()
            .WithMessage("A manifest resource mapper for section 'dashboards' is already registered.");
    }

    [Fact]
    public void ThrowingResourceMapperReturnsDiagnostic()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var registry = new ManifestResourceMapperRegistry().Add(new ThrowingMapper());

        var normalize = () => _normalizer.Normalize(manifest, registry);

        var normalized = normalize.Should().NotThrow().Subject;
        normalized.Diagnostics.Should().ContainSingle(x =>
            x.Code == ManifestDiagnosticCodes.ResourceMapperFailed &&
            x.Details.Contains(new KeyValuePair<string, string>("section", "dashboards")));
        normalized.Resources.Should().BeEmpty();
    }

    [Fact]
    public void ThrowingResourceMapperPreservesPreThrowDiagnostics()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var registry = new ManifestResourceMapperRegistry().Add(new DiagnosticThenThrowingMapper());

        var normalized = _normalizer.Normalize(manifest, registry);

        normalized.Diagnostics.Select(x => x.Code).Should().Equal(
            "dashboard.prethrow",
            ManifestDiagnosticCodes.ResourceMapperFailed);
        normalized.Resources.Should().BeEmpty();
    }

    [Fact]
    public void LazyThrowingResourceMapperDoesNotKeepPartialResources()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
            kind: EnvironmentManifest
            metadata:
              name: custom
            resources:
              dashboards:
                - id: sales
            """, ManifestFormat.Yaml).Manifest!;
        var registry = new ManifestResourceMapperRegistry().Add(new LazyThrowingMapper());

        var normalized = _normalizer.Normalize(manifest, registry);

        normalized.Diagnostics.Should().ContainSingle(x => x.Code == ManifestDiagnosticCodes.ResourceMapperFailed);
        normalized.Resources.Should().BeEmpty();
    }

    [Fact]
    public void ManifestAndResourceMetadataArePreserved()
    {
        var manifest = _reader.Read("""
            apiVersion: valence-control/v1alpha1
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

    private sealed class CapturingMapper : IManifestResourceMapper
    {
        public string SectionName => "dashboards";

        public ManifestNormalizationContext? Context { get; private set; }

        public IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context)
        {
            Context = context;
            return [];
        }
    }

    private sealed class ThrowingMapper : IManifestResourceMapper
    {
        public string SectionName => "dashboards";

        public IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context) =>
            throw new InvalidOperationException("Dashboard mapper failed.");
    }

    private sealed class DiagnosticThenThrowingMapper : IManifestResourceMapper
    {
        public string SectionName => "dashboards";

        public IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context)
        {
            context.AddDiagnostic(new DeploymentDiagnostic(
                "dashboard.prethrow",
                DeploymentDiagnosticSeverity.Warning,
                "Dashboard mapper found an issue before failing."));
            throw new InvalidOperationException("Dashboard mapper failed.");
        }
    }

    private sealed class LazyThrowingMapper : IManifestResourceMapper
    {
        public string SectionName => "dashboards";

        public IReadOnlyCollection<DeploymentResource> Map(JsonNode? section, ManifestNormalizationContext context) =>
            new LazyThrowingResources();
    }

    private sealed class LazyThrowingResources : IReadOnlyCollection<DeploymentResource>
    {
        public int Count => 1;

        public IEnumerator<DeploymentResource> GetEnumerator()
        {
            yield return new DeploymentResource(
                new DeploymentResourceId("dashboard", "partial"),
                desiredStateHash: new ArtifactDigest("sha256", "partial"));
            throw new InvalidOperationException("Lazy mapper failed.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
