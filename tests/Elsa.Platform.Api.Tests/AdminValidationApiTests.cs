using System.Net;
using Elsa.Platform.Api.Admin.Packages;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class AdminValidationApiTests
{
    [Fact]
    public async Task Admin_can_view_validation_results_for_version()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            var version = PublicCatalogSeedData.AddVersion(package, validationStatus: ValidationStatus.Invalid);
            db.PackageSources.Add(source);
            db.ManifestValidationResults.Add(new ManifestValidationResultRecord
            {
                PackageVersion = version,
                PackageVersionId = version.Id,
                Status = ValidationStatus.Invalid,
                SchemaVersion = "1.0",
                ErrorsJson = """["bad"]"""
            });
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var results = await client.GetPlatformJsonAsync<AdminValidationFindingsResponse>(
            "/api/admin/packages/Elsa.Email/versions/1.0.0/validation");

        results.Should().NotBeNull();
        results!.PackageId.Should().Be("Elsa.Email");
        results.Findings.Should().ContainSingle(x => x.Severity == "Error" && x.Message == "bad" && x.BlocksPublicVisibility);
    }

    [Fact]
    public async Task Validation_results_normalize_warning_objects_with_missing_optional_fields()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            var version = PublicCatalogSeedData.AddVersion(package);
            db.PackageSources.Add(source);
            db.ManifestValidationResults.Add(new ManifestValidationResultRecord
            {
                PackageVersion = version,
                PackageVersionId = version.Id,
                Status = ValidationStatus.Valid,
                SchemaVersion = "1.0",
                WarningsJson = """[{ "message": "description is recommended" }]""",
                ValidatorVersion = "validator-1"
            });
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var results = await client.GetPlatformJsonAsync<AdminValidationFindingsResponse>(
            "/api/admin/packages/elsa.email/versions/1.0.0/validation");

        results.Should().NotBeNull();
        results!.Findings.Should().ContainSingle(x =>
            x.Severity == "Warning"
            && x.Code == null
            && x.Path == null
            && x.Message == "description is recommended"
            && !x.BlocksPublicVisibility
            && x.ValidatorVersion == "validator-1");
    }

    [Fact]
    public async Task Validation_results_return_empty_findings_when_no_results_exist_for_indexed_version()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var results = await client.GetPlatformJsonAsync<AdminValidationFindingsResponse>(
            "/api/admin/packages/Elsa.Email/versions/1.0.0/validation");

        results.Should().NotBeNull();
        results!.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Validation_results_return_not_found_for_unknown_package_or_version()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.GetAsync("/api/admin/packages/Elsa.Email/versions/9.9.9/validation");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
