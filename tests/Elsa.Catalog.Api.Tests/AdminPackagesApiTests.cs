using System.Net;
using Elsa.Catalog.Api.Admin.Packages;
using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Testing;
using FluentAssertions;

namespace Elsa.Catalog.Api.Tests;

public sealed class AdminPackagesApiTests
{
    [Fact]
    public async Task Admin_can_review_visible_and_unapproved_packages()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source, approved: false);
            PublicCatalogSeedData.AddVersion(package);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var packages = await client.GetCatalogJsonAsync<List<AdminPackageListResponse>>("/api/admin/packages");

        packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email" && !x.Approved);
        var package = packages.Single(x => x.PackageId == "Elsa.Email");
        package.SourceId.Should().NotBeEmpty();
        package.ApprovalStatus.Should().Be(PackageApprovalStatus.Approved);
        package.ValidationStatus.Should().Be(ValidationStatus.Valid);
        package.FeaturesCount.Should().Be(0);
        package.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Package_status_summaries_use_latest_version_only()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(
                package,
                version: "1.0.0",
                validationStatus: ValidationStatus.Invalid,
                approvalStatus: PackageApprovalStatus.Rejected);
            PublicCatalogSeedData.AddVersion(package, version: "2.0.0");
            package.LatestVersion = "2.0.0";
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var packages = await client.GetCatalogJsonAsync<List<AdminPackageListResponse>>("/api/admin/packages");

        packages.Should().NotBeNull();
        var package = packages!.Single(x => x.PackageId == "Elsa.Email");
        package.ApprovalStatus.Should().Be(PackageApprovalStatus.Approved);
        package.ValidationStatus.Should().Be(ValidationStatus.Valid);
    }

    [Fact]
    public async Task Package_details_return_summary_source_canonical_casing_and_latest_version_data()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.CreateMultiVersionPackage(source);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var package = await client.GetCatalogJsonAsync<AdminPackageResponse>("/api/admin/packages/elsa.persistence.postgresql");

        package.Should().NotBeNull();
        package!.PackageId.Should().Be("Elsa.Persistence.PostgreSql");
        package.Source.Should().NotBeNull();
        package.Source!.Name.Should().Be("Test NuGet");
        package.LatestVersion.Should().Be("1.0.2");
        package.ApprovalStatus.Should().Be(PackageApprovalStatus.Pending);
        package.ValidationStatus.Should().Be(ValidationStatus.Valid);
        package.Versions.Should().ContainSingle(x => x.Version == "1.0.2" && x.VersionStateToken.Length > 0);
        package.Versions.Single(x => x.Version == "1.0.2").VisibilityReasons.Should().Contain(x => x.Code == "VersionPendingApproval");
        package.Versions.Single(x => x.Version == "1.0.2").VisibilityReasons.Should().Contain(x => x.Code == "PackagePendingApproval");
    }

    [Fact]
    public async Task Package_details_return_empty_versions_for_packages_without_indexed_versions()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.CreatePackageWithoutVersions(source);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var package = await client.GetCatalogJsonAsync<AdminPackageResponse>("/api/admin/packages/Elsa.Empty");

        package.Should().NotBeNull();
        package!.PackageId.Should().Be("Elsa.Empty");
        package.Versions.Should().BeEmpty();
        package.FeaturesCount.Should().Be(0);
        package.ValidationStatus.Should().Be(ValidationStatus.NotValidated);
    }

    [Fact]
    public async Task Package_details_return_not_found_for_unknown_packages()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.GetAsync("/api/admin/packages/Elsa.Missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Package_details_include_feature_settings_dependencies_and_manifest_metadata()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            var version = PublicCatalogSeedData.AddVersion(package, manifestHash: "sha256:email");
            PublicCatalogSeedData.AddFeature(
                version,
                dependenciesJson: """[{ "packageId": "Elsa.Core", "versionRange": "[4.0.0,5.0.0)" }]""",
                infrastructureJson: """[{ "kind": "SmtpServer", "optional": false }]""");
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var package = await client.GetCatalogJsonAsync<AdminPackageResponse>("/api/admin/packages/Elsa.Email");

        var version = package!.Versions.Single();
        version.Manifest.Available.Should().BeTrue();
        version.Manifest.ManifestHash.Should().Be("sha256:email");
        version.Manifest.ManifestJson.Should().BeEmpty();
        version.Compatibility.TargetFrameworks.Should().ContainSingle("net10.0");
        version.Features.Should().ContainSingle();
        var feature = version.Features.Single();
        feature.Settings.Should().ContainSingle(x => x.Name == "smtpHost" && x.Required);
        feature.DependenciesJson.Should().Contain("Elsa.Core");
        feature.InfrastructureJson.Should().Contain("SmtpServer");
    }

    [Fact]
    public async Task Package_version_manifest_endpoint_returns_raw_manifest_metadata()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package, manifestHash: "sha256:manifest");
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var manifest = await client.GetCatalogJsonAsync<AdminVersionManifestResponse>("/api/admin/packages/Elsa.Email/versions/1.0.0/manifest");

        manifest.Should().NotBeNull();
        manifest!.PackageId.Should().Be("Elsa.Email");
        manifest.ManifestHash.Should().Be("sha256:manifest");
        manifest.ManifestJson.Should().Contain("Elsa.Email");
    }
}
