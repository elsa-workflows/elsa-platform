using Elsa.Platform.PackageCatalog.Core.Builder;
using Elsa.Platform.PackageCatalog.Core.Builder.Planner;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Packages;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

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

        result.Resolved.Packages.Should().Contain(x => x.PackageId == "Elsa.Smtp");
        result.Resolved.Packages.Single(x => x.PackageId == "Elsa.Smtp").SelectedFeatures.Should().Contain("smtp");
        result.Resolved.Infrastructure.Should().ContainSingle(x => x.ProviderId == "postgres-compose");
        result.AutoAdded.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Smtp");
        result.AutoAdded.Features.Should().Contain("smtp");
        result.AutoAdded.Infrastructure.Should().ContainSingle(x => x.ProviderId == "postgres-compose");
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

        result.Findings.Should().Contain(x => x.Code == "feature.packageDependency");
    }

    private static BuilderPlannerService CreateService(IReadOnlyList<PublicPackageProjection> packages)
    {
        var queries = new FakeQueries(packages);
        var catalog = new PublicCatalogQueryService(queries, new PublicCatalogCache(new MemoryCache(new MemoryCacheOptions())));
        return new BuilderPlannerService(catalog, new CompatibilityCheckService(queries, new VersionRangeEvaluator()), new InfrastructureProviderCatalog());
    }

    private static PublicPackageProjection Package(PublicPackageSourceProjection source, string packageId, PublicFeatureProjection feature)
    {
        var version = new PublicPackageVersionProjection(packageId, "1.0.0", source, "1.0", null, [feature]);
        return new PublicPackageProjection(packageId, packageId, source, "1.0.0", [version]);
    }

    private static PublicFeatureProjection Feature(
        string featureId,
        IReadOnlyList<PublicDependencyProjection>? dependencies = null,
        IReadOnlyList<PublicInfrastructureRequirementProjection>? infrastructure = null) =>
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
            dependencies ?? [],
            [],
            infrastructure ?? [],
            false,
            false,
            "{}",
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
                : $"[{string.Join(",", feature.Dependencies.Select(x => $"{{ \"packageId\": \"{x.PackageId}\", \"featureId\": {(x.FeatureId is null ? "null" : $"\"{x.FeatureId}\"")} }}"))}]";
            return $"{{ \"id\": \"{feature.FeatureId}\", \"typeName\": \"{feature.TypeName}\", \"displayName\": \"{feature.DisplayName}\", \"dependencies\": {dependencies} }}";
        }
    }
}
