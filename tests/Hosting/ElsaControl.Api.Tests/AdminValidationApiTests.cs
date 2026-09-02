using System.Net;
using ElsaControl.Api.Admin.Packages;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sync;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class AdminValidationApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public AdminValidationApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Admin_can_view_validation_results_for_version()
    {
        var app = _app;
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

        var results = await client.GetControlJsonAsync<AdminValidationFindingsResponse>(
            "/api/admin/packages/Elsa.Email/versions/1.0.0/validation");

        Assert.NotNull(results);
        Assert.Equal("Elsa.Email", results!.PackageId);
        Assert.Single(results.Findings, x => x.Severity == "Error" && x.Message == "bad" && x.BlocksPublicVisibility);
    }

    [Fact]
    public async Task Validation_results_normalize_warning_objects_with_missing_optional_fields()
    {
        var app = _app;
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

        var results = await client.GetControlJsonAsync<AdminValidationFindingsResponse>(
            "/api/admin/packages/elsa.email/versions/1.0.0/validation");

        Assert.NotNull(results);
        Assert.Single(results!.Findings, x =>
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
        var app = _app;
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

        var results = await client.GetControlJsonAsync<AdminValidationFindingsResponse>(
            "/api/admin/packages/Elsa.Email/versions/1.0.0/validation");

        Assert.NotNull(results);
        Assert.Empty(results!.Findings);
    }

    [Fact]
    public async Task Validation_results_return_not_found_for_unknown_package_or_version()
    {
        var app = _app;
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

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
