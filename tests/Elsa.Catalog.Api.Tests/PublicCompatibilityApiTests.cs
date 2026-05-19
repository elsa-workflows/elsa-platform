using System.Net.Http.Json;
using Elsa.Catalog.Api.Public.Compatibility;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Testing;
using FluentAssertions;

namespace Elsa.Catalog.Api.Tests;

public sealed class PublicCompatibilityApiTests
{
    [Fact]
    public async Task Compatibility_check_returns_findings_for_incompatible_elsa_version()
    {
        await using var app = new CatalogApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            var version = PublicCatalogSeedData.AddVersion(package);
            version.ManifestJson = """
            {
              "schemaVersion": "1.0",
              "package": { "id": "Elsa.Email", "version": "1.0.0" },
              "displayName": "Email",
              "compatibility": { "elsaVersionRange": "[3.0.0,4.0.0)" }
            }
            """;
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().PostAsJsonAsync("/api/compatibility/check", new CompatibilityCheckApiRequest(
            "4.0.0",
            null,
            [new SelectedPackageVersionApiRequest(sourceId, "Elsa.Email", "1.0.0")],
            []));
        var result = await response.Content.ReadFromJsonAsync<CompatibilityCheckApiResponse>();

        result!.Compatible.Should().BeFalse();
        result.Findings.Should().ContainSingle(x => x.Code == "compatibility.elsa");
    }

    [Fact]
    public async Task Compatibility_check_treats_non_browseable_source_versions_as_missing()
    {
        await using var app = new CatalogApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Browseable = false;
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package, validationStatus: ValidationStatus.Invalid, approvalStatus: PackageApprovalStatus.Rejected, suspicious: true);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().PostAsJsonAsync("/api/compatibility/check", new CompatibilityCheckApiRequest(
            null,
            null,
            [new SelectedPackageVersionApiRequest(sourceId, "Elsa.Email", "1.0.0")],
            []));
        var result = await response.Content.ReadFromJsonAsync<CompatibilityCheckApiResponse>();

        result!.Compatible.Should().BeFalse();
        result.Findings.Should().ContainSingle(x => x.Code == "package.missing");
        result.Findings.Select(x => x.Code).Should().NotContain(["package.invalid", "package.notApproved", "package.suspicious"]);
    }
}
