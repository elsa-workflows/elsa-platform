using ValenceControl.RuntimeBuilder.Abstractions;
using ValenceControl.RuntimeBuilder.Abstractions.Planner;
using ValenceControl.PackageCatalog.Abstractions.Catalog;
using ValenceControl.PackageCatalog.Core.Compatibility;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.RuntimeBuilder.Core.Builder;
using ValenceControl.RuntimeBuilder.Core.Builder.Planner;
using Microsoft.Extensions.Caching.Memory;

namespace ValenceControl.RuntimeBuilder.Core.Tests;

public sealed class BuilderPlannerTests
{
    [Fact]
    public async Task Planner_auto_adds_package_feature_dependencies_and_infrastructure()
    {
        var source = new PublicPackageSourceProjection(Guid.NewGuid(), "NuGet", "https://example.test/v3/index.json");
        var email = Package(source, "Elsa.Email", Feature(
            "email",
            dependencies: [new PublicDependencyProjection("Elsa.Smtp", null, "smtp", false, null)],
            infrastructure: [new PublicInfrastructureRequirementProjection("database", "database", false, null, [], ["postgres"], [], "{}")]));
        var smtp = Package(source, "Elsa.Smtp", Feature("smtp"));
        var service = CreateService([email, smtp]);

        var result = await service.PlanAsync(new BuilderPlanRequest(new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BundlePackageSelection(source.Id, "Elsa.Email", "1.0.0", ["email"], null)],
            [new PackageSourceSelection(source.Id)],
            [],
            new LocalPackagesOptions(false, "packages"))));

        Assert.Contains(result.Resolved.Packages, x => x.PackageId == "Elsa.Smtp");
        Assert.Contains("smtp", result.Resolved.Packages.Single(x => x.PackageId == "Elsa.Smtp").SelectedFeatures!);
        Assert.Single(result.Resolved.Infrastructure, x => x.ProviderId == "postgres-compose");
        Assert.Single(result.AutoAdded.Packages, x => x.PackageId == "Elsa.Smtp");
        Assert.Contains("smtp", result.AutoAdded.Features);
        Assert.Single(result.AutoAdded.Infrastructure, x => x.ProviderId == "postgres-compose");
    }

    [Fact]
    public async Task Planner_auto_adds_feature_dependency_resolved_by_shell_feature_alias()
    {
        var source = new PublicPackageSourceProjection(Guid.NewGuid(), "NuGet", "https://example.test/v3/index.json");
        var core = Package(source, "Elsa", Feature("Elsa.Elsa", dependencies: [new PublicDependencyProjection(null, null, "Elsa.WorkflowManagement", false, null)]));
        var workflowManagement = Package(
            source,
            "Elsa.Workflows.Management",
            Feature(
                "Elsa.Workflows.Management.WorkflowManagement",
                extensionsJson: """{ "cshellsFeatureName": "WorkflowManagement" }"""));
        var service = CreateService([core, workflowManagement]);

        var result = await service.PlanAsync(new BuilderPlanRequest(new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BundlePackageSelection(source.Id, "Elsa", "1.0.0", ["Elsa.Elsa"], null)],
            [new PackageSourceSelection(source.Id)],
            [],
            new LocalPackagesOptions(false, "packages"))));

        Assert.Contains(result.Resolved.Packages, x => x.PackageId == "Elsa.Workflows.Management");
        Assert.Contains("Elsa.Workflows.Management.WorkflowManagement", result.Resolved.Packages.Single(x => x.PackageId == "Elsa.Workflows.Management").SelectedFeatures!);
        Assert.Contains("Elsa.Workflows.Management.WorkflowManagement", result.AutoAdded.Features);
        Assert.DoesNotContain(result.Findings, x => new[] { "feature.missing", "feature.dependency" }.Contains(x.Code));
    }

    [Fact]
    public async Task Planner_returns_compatibility_findings_for_resolved_state()
    {
        var source = new PublicPackageSourceProjection(Guid.NewGuid(), "NuGet", "https://example.test/v3/index.json");
        var email = Package(source, "Elsa.Email", Feature("email", dependencies: [new PublicDependencyProjection("Elsa.Missing", null, null, false, null)]));
        var service = CreateService([email]);

        var result = await service.PlanAsync(new BuilderPlanRequest(new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [new BundlePackageSelection(source.Id, "Elsa.Email", "1.0.0", ["email"], null)],
            [new PackageSourceSelection(source.Id)],
            [],
            new LocalPackagesOptions(false, "packages"))));

        Assert.Contains(result.Findings, x => x.Code == "feature.packageDependency");
    }

    [Fact]
    public async Task Planner_returns_runtime_kind_findings_for_incompatible_selected_features()
    {
        var source = new PublicPackageSourceProjection(Guid.NewGuid(), "NuGet", "https://example.test/v3/index.json");
        var package = Package(source, "Elsa.StudioOnly", Feature("studio-feature", runtimeKinds: ["elsa.studio"]));
        var service = CreateService([package]);

        var result = await service.PlanAsync(new BuilderPlanRequest(new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-server", "latest", 8080, new Dictionary<string, string>()),
            [new BundlePackageSelection(source.Id, "Elsa.StudioOnly", "1.0.0", ["studio-feature"], null)],
            [new PackageSourceSelection(source.Id)],
            [],
            new LocalPackagesOptions(false, "packages"))));

        Assert.Single(result.Findings, x => x.Code == "feature.runtimeKindUnsupported");
    }

    private static BuilderPlannerService CreateService(IReadOnlyList<PublicPackageProjection> packages)
    {
        var queries = new FakeQueries(packages);
        return new BuilderPlannerService(queries, new CompatibilityCheckService(queries, new VersionRangeEvaluator()), new RuntimeImageCatalog(), new InfrastructureProviderCatalog());
    }

    private static PublicPackageProjection Package(PublicPackageSourceProjection source, string packageId, PublicFeatureProjection feature)
    {
        var version = new PublicPackageVersionProjection(packageId, "1.0.0", source, "1.0", ["elsa.server"], null, [feature]);
        return new PublicPackageProjection(packageId, packageId, source, ["elsa.server"], "1.0.0", [version]);
    }

    private static PublicFeatureProjection Feature(
        string featureId,
        IReadOnlyList<string>? runtimeKinds = null,
        IReadOnlyList<PublicDependencyProjection>? dependencies = null,
        IReadOnlyList<PublicInfrastructureRequirementProjection>? infrastructure = null,
        string extensionsJson = "{}") =>
        new(
            featureId,
            "",
            "1.0.0",
            new PublicPackageSourceProjection(Guid.Empty, "", ""),
            $"Features.{featureId}",
            featureId,
            null,
            null,
            [],
            [],
            runtimeKinds ?? ["elsa.server"],
            dependencies ?? [],
            [],
            infrastructure ?? [],
            false,
            false,
            extensionsJson,
            []);

    private sealed class FakeQueries(IReadOnlyList<PublicPackageProjection> packages) : IPublicCatalogQueries, ICompatibilityQueries
    {
        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicPackageProjection>>(packages.Where(x => sourceIds.Count == 0 || sourceIds.Contains(x.Source.Id)).ToList());

        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default) =>
            ListPackagesAsync(sourceIds, cancellationToken);

        public Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(packages.SingleOrDefault(x => x.Source.Id == sourceId && x.PackageId == packageId));

        public Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            GetPackageAsync(sourceId, packageId, cancellationToken);

        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicPackageVersionProjection>>(packages.SingleOrDefault(x => x.Source.Id == sourceId && x.PackageId == packageId)?.Versions ?? []);

        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            ListVersionsAsync(sourceId, packageId, cancellationToken);

        public Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(packages.SingleOrDefault(x => x.Source.Id == sourceId && x.PackageId == packageId)?.Versions.SingleOrDefault(x => x.Version == version));

        public Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
            GetVersionAsync(sourceId, packageId, version, cancellationToken);

        public Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicFeatureProjection>>(packages.SelectMany(x => x.Versions).SelectMany(x => x.Features).ToList());

        public Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default) =>
            Task.FromResult(packages.SelectMany(x => x.Versions).SelectMany(x => x.Features).FirstOrDefault(x => x.FeatureId == featureId));

        public Task<PackageVersion?> GetPackageVersionAsync(Guid? workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default)
        {
            var package = packages.SingleOrDefault(x => x.Source.Id == sourceId && x.PackageId == packageId);
            if (package is null)
                return Task.FromResult<PackageVersion?>(null);

            var domainPackage = new Package { PackageId = packageId, SourceId = sourceId, Approved = true, Listed = true };
            return Task.FromResult<PackageVersion?>(new PackageVersion
            {
                Package = domainPackage,
                Version = version,
                ManifestJson = $$"""
                {
                  "schemaVersion": "1.0",
                  "package": { "id": "{{packageId}}", "version": "{{version}}" },
                  "features": [
                    {{string.Join(",", package.Versions.Single().Features.Select(ToManifestFeatureJson))}}
                  ]
                }
                """,
                ValidationStatus = ValidationStatus.Valid,
                ApprovalStatus = PackageApprovalStatus.Approved,
                IsListed = true
            });
        }

        private static string ToManifestFeatureJson(PublicFeatureProjection feature)
        {
            var dependencies = feature.Dependencies.Count == 0
                ? "[]"
                : $"[{string.Join(",", feature.Dependencies.Select(x => $"{{ \"packageId\": {JsonString(x.PackageId)}, \"featureId\": {JsonString(x.FeatureId)} }}"))}]";
            var compatibility = feature.RuntimeKinds.Count == 0
                ? ""
                : $", \"compatibility\": {{ \"runtimeKinds\": [{string.Join(",", feature.RuntimeKinds.Select(x => $"\"{x}\""))}] }}";
            var extensions = feature.ExtensionsJson == "{}"
                ? ""
                : $", \"extensions\": {feature.ExtensionsJson}";
            return $"{{ \"id\": \"{feature.FeatureId}\", \"typeName\": \"{feature.TypeName}\", \"displayName\": \"{feature.DisplayName}\", \"dependencies\": {dependencies}{compatibility}{extensions} }}";
        }

        private static string JsonString(string? value) =>
            value is null ? "null" : $"\"{value}\"";
    }
}
