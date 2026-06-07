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

    [Fact]
    public async Task Satisfies_feature_dependency_with_shell_feature_alias()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var core = PublicCatalogSeedData.CreatePackage(source, "Elsa");
        var management = PublicCatalogSeedData.CreatePackage(source, "Elsa.Workflows.Management");
        var coreVersion = PublicCatalogSeedData.AddVersion(core);
        var managementVersion = PublicCatalogSeedData.AddVersion(management);
        coreVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa", "version": "1.0.0" },
          "displayName": "Elsa",
          "features": [
            {
              "id": "Elsa.Elsa",
              "typeName": "Elsa.ElsaFeature",
              "displayName": "Elsa Core",
              "dependencies": [{ "featureId": "Elsa.WorkflowManagement" }]
            }
          ]
        }
        """;
        managementVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Workflows.Management", "version": "1.0.0" },
          "displayName": "Workflow Management",
          "features": [
            {
              "id": "Elsa.Workflows.Management.WorkflowManagement",
              "typeName": "Elsa.Workflows.Management.WorkflowManagementFeature",
              "displayName": "Workflow Management",
              "extensions": { "cshellsFeatureName": "WorkflowManagement" }
            }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(core.Versions.Concat(management.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(
            null,
            null,
            [Selection(source, "Elsa"), Selection(source, "Elsa.Workflows.Management")],
            ["Elsa.Elsa", "Elsa.Workflows.Management.WorkflowManagement"]));

        result.Findings.Should().NotContain(x => x.Code == "feature.missing" || x.Code == "feature.dependency");
    }

    [Fact]
    public async Task Runtime_kind_validation_rejects_legacy_features_without_runtime_metadata_when_runtime_context_is_supplied()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Mixed");
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Mixed", "version": "1.0.0" },
          "displayName": "Mixed",
          "features": [
            { "id": "server", "typeName": "Elsa.ServerFeature", "displayName": "Server", "compatibility": { "runtimeKinds": ["elsa.server"] } },
            { "id": "legacy", "typeName": "Elsa.LegacyFeature", "displayName": "Legacy" }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Mixed")], ["server", "legacy"], RuntimeKinds: ["elsa.server"]));

        result.Findings.Should().ContainSingle(x => x.Code == "feature.runtimeKindUnsupported" && x.Message.Contains("legacy"));
    }

    [Fact]
    public async Task Runtime_kind_validation_is_skipped_without_runtime_context()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Legacy");
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Legacy", "version": "1.0.0" },
          "displayName": "Legacy",
          "features": [
            { "id": "legacy", "typeName": "Elsa.LegacyFeature", "displayName": "Legacy" }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Legacy")], ["legacy"]));

        result.Findings.Should().NotContain(x => x.Code == "feature.runtimeKindUnsupported");
    }

    [Fact]
    public async Task Runtime_kind_validation_reports_incompatible_selected_features()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Studio");
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Studio", "version": "1.0.0" },
          "displayName": "Studio",
          "features": [
            { "id": "studio", "typeName": "Elsa.StudioFeature", "displayName": "Studio", "compatibility": { "runtimeKinds": ["elsa.studio"] } }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Studio")], ["studio"], RuntimeKinds: ["elsa.server"]));

        result.Findings.Should().ContainSingle(x => x.Code == "feature.runtimeKindUnsupported");
    }

    [Fact]
    public async Task Runtime_kind_validation_treats_combined_runtime_as_matching_either_kind()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Server");
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Server", "version": "1.0.0" },
          "displayName": "Server",
          "features": [
            { "id": "server", "typeName": "Elsa.ServerFeature", "displayName": "Server", "compatibility": { "runtimeKinds": ["elsa.server"] } }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Server")], ["server"], RuntimeKinds: ["elsa.server", "elsa.studio"]));

        result.Findings.Should().NotContain(x => x.Code == "feature.runtimeKindUnsupported");
    }

    [Fact]
    public async Task Runtime_kind_validation_uses_package_level_kind_unless_feature_overrides_it()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Specialized");
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Specialized", "version": "1.0.0" },
          "displayName": "Specialized",
          "compatibility": { "runtimeKinds": ["elsa.server"] },
          "features": [
            { "id": "inherits-package", "typeName": "Elsa.InheritsPackageFeature", "displayName": "Inherits Package" },
            { "id": "narrows-to-studio", "typeName": "Elsa.NarrowsToStudioFeature", "displayName": "Narrows To Studio", "compatibility": { "runtimeKinds": ["elsa.studio"] } }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(package.Versions), new VersionRangeEvaluator());

        var serverResult = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Specialized")], ["inherits-package", "narrows-to-studio"], RuntimeKinds: ["elsa.server"]));
        var studioResult = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Specialized")], ["inherits-package", "narrows-to-studio"], RuntimeKinds: ["elsa.studio"]));

        serverResult.Findings.Should().ContainSingle(x => x.Code == "feature.runtimeKindUnsupported" && x.Message.Contains("narrows-to-studio"));
        studioResult.Findings.Should().ContainSingle(x => x.Code == "feature.runtimeKindUnsupported" && x.Message.Contains("inherits-package"));
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
