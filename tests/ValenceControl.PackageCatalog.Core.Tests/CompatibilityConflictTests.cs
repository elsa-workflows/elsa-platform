using ValenceControl.PackageCatalog.Abstractions.Compatibility;
using ValenceControl.PackageCatalog.Core.Compatibility;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageCatalog.Testing;
using FluentAssertions;

namespace ValenceControl.PackageCatalog.Core.Tests;

public sealed class CompatibilityConflictTests
{
    [Fact]
    public async Task Reports_direct_package_conflicts_from_manifest()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var email = PublicCatalogSeedData.CreatePackage(source, "Elsa.Email");
        var sms = PublicCatalogSeedData.CreatePackage(source, "Elsa.Sms");
        var emailVersion = PublicCatalogSeedData.AddVersion(email);
        PublicCatalogSeedData.AddVersion(sms);
        emailVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "conflicts": [{ "packageId": "Elsa.Sms" }]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(email.Versions.Concat(sms.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Email"), Selection(source, "Elsa.Sms")], []));

        result.Findings.Should().ContainSingle(x => x.Code == "package.conflict");
    }

    [Fact]
    public async Task Ignores_package_conflicts_when_selected_version_is_outside_conflict_range()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var email = PublicCatalogSeedData.CreatePackage(source, "Elsa.Email");
        var sms = PublicCatalogSeedData.CreatePackage(source, "Elsa.Sms");
        var emailVersion = PublicCatalogSeedData.AddVersion(email);
        PublicCatalogSeedData.AddVersion(sms);
        emailVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "conflicts": [{ "packageId": "Elsa.Sms", "versionRange": ">=2.0.0" }]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(email.Versions.Concat(sms.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Email"), Selection(source, "Elsa.Sms")], []));

        result.Findings.Should().NotContain(x => x.Code == "package.conflict");
    }

    [Fact]
    public async Task Ignores_feature_conflicts_when_selected_package_version_is_outside_conflict_range()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var email = PublicCatalogSeedData.CreatePackage(source, "Elsa.Email");
        var sms = PublicCatalogSeedData.CreatePackage(source, "Elsa.Sms");
        var emailVersion = PublicCatalogSeedData.AddVersion(email);
        var smsVersion = PublicCatalogSeedData.AddVersion(sms);
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
              "conflicts": [{ "packageId": "Elsa.Sms", "versionRange": ">=2.0.0", "featureId": "sms" }]
            }
          ]
        }
        """;
        smsVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Sms", "version": "1.0.0" },
          "displayName": "SMS",
          "features": [
            { "id": "sms", "typeName": "Elsa.Sms.SmsFeature", "displayName": "SMS" }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(email.Versions.Concat(sms.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(source, "Elsa.Email"), Selection(source, "Elsa.Sms")], ["email", "sms"]));

        result.Findings.Should().NotContain(x => x.Code == "feature.conflict");
    }

    [Fact]
    public async Task Reports_package_conflicts_against_matching_package_id_from_another_source()
    {
        var sourceA = PublicCatalogSeedData.CreatePackageSource();
        var sourceB = PublicCatalogSeedData.CreatePackageSource();
        var emailA = PublicCatalogSeedData.CreatePackage(sourceA, "Elsa.Email");
        var emailB = PublicCatalogSeedData.CreatePackage(sourceB, "Elsa.Email");
        var emailAVersion = PublicCatalogSeedData.AddVersion(emailA, "1.0.0");
        PublicCatalogSeedData.AddVersion(emailB, "2.0.0");
        emailAVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Email", "version": "1.0.0" },
          "displayName": "Email",
          "conflicts": [{ "packageId": "Elsa.Email", "versionRange": ">=2.0.0" }]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(emailA.Versions.Concat(emailB.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(sourceA, "Elsa.Email", "1.0.0"), Selection(sourceB, "Elsa.Email", "2.0.0")], []));

        result.Findings.Should().ContainSingle(x => x.Code == "package.conflict");
    }

    [Fact]
    public async Task Reports_feature_conflicts_against_matching_package_feature_from_another_source()
    {
        var sourceA = PublicCatalogSeedData.CreatePackageSource();
        var sourceB = PublicCatalogSeedData.CreatePackageSource();
        var email = PublicCatalogSeedData.CreatePackage(sourceA, "Elsa.Email");
        var sms = PublicCatalogSeedData.CreatePackage(sourceB, "Elsa.Sms");
        var emailVersion = PublicCatalogSeedData.AddVersion(email);
        var smsVersion = PublicCatalogSeedData.AddVersion(sms);
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
              "conflicts": [{ "packageId": "Elsa.Sms", "featureId": "sms" }]
            }
          ]
        }
        """;
        smsVersion.ManifestJson = """
        {
          "schemaVersion": "1.0",
          "package": { "id": "Elsa.Sms", "version": "1.0.0" },
          "displayName": "SMS",
          "features": [
            { "id": "sms", "typeName": "Elsa.Sms.SmsFeature", "displayName": "SMS" }
          ]
        }
        """;
        var service = new CompatibilityCheckService(new FakeQueries(email.Versions.Concat(sms.Versions).ToList()), new VersionRangeEvaluator());

        var result = await service.CheckAsync(new CompatibilityCheckRequest(null, null, [Selection(sourceA, "Elsa.Email"), Selection(sourceB, "Elsa.Sms")], ["email", "sms"]));

        result.Findings.Should().ContainSingle(x => x.Code == "feature.conflict");
    }

    private static SelectedPackageVersion Selection(PackageSource source, string packageId, string version = "1.0.0") =>
        new(source.Id, packageId, version);

    private sealed class FakeQueries(IReadOnlyList<PackageVersion> versions) : ICompatibilityQueries
    {
        public Task<PackageVersion?> GetPackageVersionAsync(Guid? workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.SingleOrDefault(x => x.Package?.SourceId == sourceId && x.Package.PackageId == packageId && x.Version == version));
    }
}
