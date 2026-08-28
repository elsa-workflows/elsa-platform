using System.Net;
using ElsaControl.Api.Admin.Packages;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class AdminPackagesApiTests
{
    [Fact]
    public async Task Admin_can_review_visible_and_unapproved_packages()
    {
        await using var app = new ControlApiTestApplication();
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

        var packages = await client.GetControlJsonAsync<List<AdminPackageListResponse>>("/api/admin/packages");

        Assert.NotNull(packages);
        Assert.Single(packages!, x => x.PackageId == "Elsa.Email" && !x.Approved);
        var package = packages!.Single(x => x.PackageId == "Elsa.Email");
        Assert.NotNull(package.SourceId);
        Assert.NotEqual(Guid.Empty, package.SourceId.Value);
        Assert.Equal(PackageApprovalStatus.Approved, package.ApprovalStatus);
        Assert.Equal(ValidationStatus.Valid, package.ValidationStatus);
        Assert.Equal(0, package.FeaturesCount);
        Assert.NotEqual(default, package.UpdatedAt);
    }

    [Fact]
    public async Task Package_status_summaries_use_latest_version_only()
    {
        await using var app = new ControlApiTestApplication();
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

        var packages = await client.GetControlJsonAsync<List<AdminPackageListResponse>>("/api/admin/packages");

        Assert.NotNull(packages);
        var package = packages!.Single(x => x.PackageId == "Elsa.Email");
        Assert.Equal(PackageApprovalStatus.Approved, package.ApprovalStatus);
        Assert.Equal(ValidationStatus.Valid, package.ValidationStatus);
    }

    [Fact]
    public async Task Package_details_return_summary_source_canonical_casing_and_latest_version_data()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.CreateMultiVersionPackage(source);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var package = await client.GetControlJsonAsync<AdminPackageResponse>("/api/admin/packages/elsa.persistence.postgresql");

        Assert.NotNull(package);
        Assert.Equal("Elsa.Persistence.PostgreSql", package!.PackageId);
        Assert.NotNull(package.Source);
        Assert.Equal("Test NuGet", package.Source!.Name);
        Assert.Equal("1.0.2", package.LatestVersion);
        Assert.Equal(PackageApprovalStatus.Pending, package.ApprovalStatus);
        Assert.Equal(ValidationStatus.Valid, package.ValidationStatus);
        Assert.Single(package.Versions, x => x.Version == "1.0.2" && x.VersionStateToken.Length > 0);
        Assert.Contains(package.Versions.Single(x => x.Version == "1.0.2").VisibilityReasons, x => x.Code == "VersionPendingApproval");
        Assert.Contains(package.Versions.Single(x => x.Version == "1.0.2").VisibilityReasons, x => x.Code == "PackagePendingApproval");
    }

    [Fact]
    public async Task Package_details_return_empty_versions_for_packages_without_indexed_versions()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.CreatePackageWithoutVersions(source);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var package = await client.GetControlJsonAsync<AdminPackageResponse>("/api/admin/packages/Elsa.Empty");

        Assert.NotNull(package);
        Assert.Equal("Elsa.Empty", package!.PackageId);
        Assert.Empty(package.Versions);
        Assert.Equal(0, package.FeaturesCount);
        Assert.Equal(ValidationStatus.NotValidated, package.ValidationStatus);
    }

    [Fact]
    public async Task Package_details_return_not_found_for_unknown_packages()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.GetAsync("/api/admin/packages/Elsa.Missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Package_details_include_feature_settings_dependencies_and_manifest_metadata()
    {
        await using var app = new ControlApiTestApplication();
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

        var package = await client.GetControlJsonAsync<AdminPackageResponse>("/api/admin/packages/Elsa.Email");

        var version = package!.Versions.Single();
        Assert.True(version.Manifest.Available);
        Assert.Equal("sha256:email", version.Manifest.ManifestHash);
        Assert.Empty(version.Manifest.ManifestJson);
        Assert.Single(version.Compatibility.TargetFrameworks, "net10.0");
        Assert.Single(version.Features);
        var feature = version.Features.Single();
        Assert.Single(feature.Settings, x => x.Name == "smtpHost" && x.Required);
        Assert.Contains("Elsa.Core", feature.DependenciesJson);
        Assert.Contains("SmtpServer", feature.InfrastructureJson);
    }

    [Fact]
    public async Task Package_version_manifest_endpoint_returns_raw_manifest_metadata()
    {
        await using var app = new ControlApiTestApplication();
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

        var manifest = await client.GetControlJsonAsync<AdminVersionManifestResponse>("/api/admin/packages/Elsa.Email/versions/1.0.0/manifest");

        Assert.NotNull(manifest);
        Assert.Equal("Elsa.Email", manifest!.PackageId);
        Assert.Equal("sha256:manifest", manifest.ManifestHash);
        Assert.Contains("Elsa.Email", manifest.ManifestJson);
    }
}
