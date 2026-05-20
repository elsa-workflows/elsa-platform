using Elsa.Platform.PackageCatalog.Abstractions.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class CompatibilityCheckServiceTests
{
    [Fact]
    public async Task Reports_missing_unapproved_invalid_and_suspicious_versions()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source);
        PublicCatalogSeedData.AddVersion(package, "1.0.0", validationStatus: ValidationStatus.Invalid, approvalStatus: PackageApprovalStatus.Rejected, suspicious: true);
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest("1.0.0", null, [Selection(source, "Elsa.Email"), Selection(source, "Missing")], []));

        result.Compatible.Should().BeFalse();
        result.Findings.Should().Contain(x => x.Code == "package.missing");
        result.Findings.Should().Contain(x => x.Code == "package.invalid");
        result.Findings.Should().Contain(x => x.Code == "package.suspicious");
        result.Findings.Should().Contain(x => x.Code == "package.notApproved");
    }

    [Fact]
    public async Task Does_not_parse_manifest_json_for_invalid_versions()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source);
        var version = PublicCatalogSeedData.AddVersion(package, validationStatus: ValidationStatus.Invalid);
        version.ManifestJson = "{";
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest("1.0.0", null, [Selection(source, "Elsa.Email")], []));

        result.Compatible.Should().BeFalse();
        result.Findings.Should().ContainSingle(x => x.Code == "package.invalid");
        result.Findings.Should().NotContain(x => x.Code == "manifest.invalidJson");
    }

    [Fact]
    public async Task Reports_invalid_json_for_valid_versions_defensively()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source);
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = "{";
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest("1.0.0", null, [Selection(source, "Elsa.Email")], []));

        result.Compatible.Should().BeFalse();
        result.Findings.Should().ContainSingle(x => x.Code == "manifest.invalidJson");
    }

    [Fact]
    public async Task Reports_invalid_package_selection_without_querying_or_throwing()
    {
        var queries = new FakeQueries([]);
        var service = new CompatibilityCheckService(queries, new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest("1.0.0", null, [new(Guid.Empty, null!, "1.0.0"), new(Guid.NewGuid(), "Elsa.Email", "")], []));

        result.Compatible.Should().BeFalse();
        queries.CallCount.Should().Be(0);
        result.Findings.Should().HaveCount(2);
        result.Findings.Should().OnlyContain(x => x.Code == "package.invalidSelection");
    }

    [Fact]
    public async Task Reports_missing_package_dependency_for_selected_feature()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source);
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            {
              "id": "email",
              "typeName": "Elsa.Email.EmailFeature",
              "displayName": "Email",
              "dependencies": [{ "packageId": "Elsa.Smtp" }]
            }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Email")], ["email"]));

        result.Findings.Should().ContainSingle(x => x.Code == "feature.packageDependency");
    }

    [Fact]
    public async Task Checks_feature_dependency_package_version_range()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var email = PublicCatalogSeedData.CreatePackage(source);
        var smtp = PublicCatalogSeedData.CreatePackage(source, "Elsa.Smtp");
        var emailVersion = PublicCatalogSeedData.AddVersion(email);
        PublicCatalogSeedData.AddVersion(smtp, "1.0.0");
        emailVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            {
              "id": "email",
              "typeName": "Elsa.Email.EmailFeature",
              "displayName": "Email",
              "dependencies": [{ "packageId": "Elsa.Smtp", "versionRange": ">=2.0.0" }]
            }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(email.Versions.Concat(smtp.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Email"), Selection(source, "Elsa.Smtp")], ["email"]));

        result.Findings.Should().ContainSingle(x => x.Code == "feature.packageDependency");
    }

    [Fact]
    public async Task Satisfies_feature_dependency_with_matching_package_from_another_source()
    {
        var sourceA = PublicCatalogSeedData.CreatePackageSource();
        var sourceB = PublicCatalogSeedData.CreatePackageSource();
        var email = PublicCatalogSeedData.CreatePackage(sourceA);
        var smtp = PublicCatalogSeedData.CreatePackage(sourceB, "Elsa.Smtp");
        var emailVersion = PublicCatalogSeedData.AddVersion(email);
        PublicCatalogSeedData.AddVersion(smtp, "2.0.0");
        emailVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "features": [
            {
              "id": "email",
              "typeName": "Elsa.Email.EmailFeature",
              "displayName": "Email",
              "dependencies": [{ "packageId": "Elsa.Smtp", "versionRange": ">=2.0.0" }]
            }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(email.Versions.Concat(smtp.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(sourceA, "Elsa.Email"), Selection(sourceB, "Elsa.Smtp", "2.0.0")], ["email"]));

        result.Findings.Should().NotContain(x => x.Code == "feature.packageDependency");
    }

    private static SelectedPackageVersion Selection(PackageSource source, string packageId, string version = "1.0.0") =>
        new(source.Id, packageId, version);

    private sealed class FakeQueries(IReadOnlyList<PackageVersion> versions) : ICompatibilityQueries
    {
        public int CallCount { get; private set; }

        public Task<PackageVersion?> GetPackageVersionAsync(Guid? workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(versions.SingleOrDefault(x => x.Package?.SourceId == sourceId && x.Package.PackageId == packageId && x.Version == version));
        }
    }
}
