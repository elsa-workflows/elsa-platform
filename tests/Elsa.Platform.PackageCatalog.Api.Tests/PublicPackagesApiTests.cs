using System.Net;
using System.Net.Http.Json;
using Elsa.Platform.PackageCatalog.Api.Public.Packages;
using Elsa.Platform.PackageCatalog.Api.Public.Sources;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

public sealed class PublicPackagesApiTests
{
    [Fact]
    public async Task Get_packages_returns_only_visible_packages()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var visible = PublicCatalogSeedData.CreatePackage(source);
            var rejected = PublicCatalogSeedData.CreatePackage(source, "Elsa.Rejected", approved: false);

            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(visible));
            PublicCatalogSeedData.AddVersion(rejected);

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var packages = await app.CreateClient().GetFromJsonAsync<List<PublicPackageResponse>>("/api/packages");

        packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email");
        packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email" && x.DisplayName == "Email");
        packages.Should().NotContain(x => x.PackageId == "Elsa.Rejected");
    }

    [Fact]
    public async Task Get_public_sources_returns_sanitized_indexed_sources()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Url = "https://user:secret@example.test/v3/index.json?token=secret#fragment";
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package);

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var sources = await app.CreateClient().GetFromJsonAsync<List<PublicSourceResponse>>("/api/sources");

        var source = sources.Should().ContainSingle().Subject;
        source.Url.Should().Be("https://example.test/v3/index.json");
        source.PackageCount.Should().Be(1);
    }

    [Fact]
    public async Task Get_public_sources_excludes_non_browseable_sources()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(db =>
        {
            var publicSource = PublicCatalogSeedData.CreatePackageSource();
            publicSource.Name = "Public";
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(publicSource, "Elsa.Public"));

            var internalSource = PublicCatalogSeedData.CreatePackageSource();
            internalSource.Name = "Internal";
            internalSource.Browseable = false;
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(internalSource, "Elsa.Internal"));

            db.PackageSources.AddRange(publicSource, internalSource);
            return Task.CompletedTask;
        });

        var sources = await app.CreateClient().GetFromJsonAsync<List<PublicSourceResponse>>("/api/sources");

        sources.Should().ContainSingle(x => x.Name == "Public");
        sources.Should().NotContain(x => x.Name == "Internal");
    }

    [Fact]
    public async Task Get_packages_filters_by_selected_source_ids()
    {
        await using var app = new CatalogApiTestApplication();
        var selectedSourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var selectedSource = PublicCatalogSeedData.CreatePackageSource();
            selectedSource.Name = "Selected";
            selectedSourceId = selectedSource.Id;
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(selectedSource, "Elsa.Selected"));

            var hiddenSource = PublicCatalogSeedData.CreatePackageSource();
            hiddenSource.Name = "Hidden";
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(hiddenSource, "Elsa.Hidden"));

            db.PackageSources.AddRange(selectedSource, hiddenSource);
            return Task.CompletedTask;
        });

        var packages = await app.CreateClient().GetFromJsonAsync<List<PublicPackageResponse>>($"/api/packages?sourceIds={selectedSourceId}");

        packages.Should().ContainSingle(x => x.PackageId == "Elsa.Selected");
        packages.Should().NotContain(x => x.PackageId == "Elsa.Hidden");
    }

    [Fact]
    public async Task Get_packages_excludes_non_browseable_sources_even_when_selected()
    {
        await using var app = new CatalogApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Browseable = false;
            sourceId = source.Id;
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(source));

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var packages = await app.CreateClient().GetFromJsonAsync<List<PublicPackageResponse>>($"/api/packages?sourceIds={sourceId}");
        var details = await app.CreateClient().GetAsync($"/api/sources/{sourceId}/packages/Elsa.Email");

        packages.Should().BeEmpty();
        details.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_package_resolves_duplicate_package_id_by_source()
    {
        await using var app = new CatalogApiTestApplication();
        var selectedSourceId = Guid.Empty;
        var otherSourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var selectedSource = PublicCatalogSeedData.CreatePackageSource();
            selectedSource.Name = "Selected";
            selectedSourceId = selectedSource.Id;
            var selectedPackage = PublicCatalogSeedData.CreatePackage(selectedSource);
            PublicCatalogSeedData.AddVersion(selectedPackage, "1.0.0");

            var otherSource = PublicCatalogSeedData.CreatePackageSource();
            otherSource.Name = "Other";
            otherSourceId = otherSource.Id;
            var otherPackage = PublicCatalogSeedData.CreatePackage(otherSource);
            PublicCatalogSeedData.AddVersion(otherPackage, "2.0.0");

            db.PackageSources.AddRange(selectedSource, otherSource);
            return Task.CompletedTask;
        });

        var selected = await app.CreateClient().GetFromJsonAsync<PublicPackageResponse>($"/api/sources/{selectedSourceId}/packages/Elsa.Email");
        var other = await app.CreateClient().GetFromJsonAsync<PublicPackageResponse>($"/api/sources/{otherSourceId}/packages/Elsa.Email");

        selected!.Source.Id.Should().Be(selectedSourceId);
        selected.Versions.Should().ContainSingle(x => x.Version == "1.0.0");
        other!.Source.Id.Should().Be(otherSourceId);
        other.Versions.Should().ContainSingle(x => x.Version == "2.0.0");
    }

    [Fact]
    public async Task Get_packages_hides_invalid_unlisted_rejected_and_suspicious_versions()
    {
        await using var app = new CatalogApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            PublicCatalogSeedData.AddVersion(package, "1.0.1", validationStatus: ValidationStatus.Invalid);
            PublicCatalogSeedData.AddVersion(package, "1.0.2", approvalStatus: PackageApprovalStatus.Rejected);
            PublicCatalogSeedData.AddVersion(package, "1.0.3", listed: false);
            PublicCatalogSeedData.AddVersion(package, "1.0.4", suspicious: true);

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var package = await app.CreateClient().GetFromJsonAsync<PublicPackageResponse>($"/api/sources/{sourceId}/packages/Elsa.Email");

        package!.Versions.Should().ContainSingle(x => x.Version == "1.0.0");
    }

    [Fact]
    public async Task Get_package_returns_not_found_for_hidden_package()
    {
        await using var app = new CatalogApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source, approved: false);
            PublicCatalogSeedData.AddVersion(package);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().GetAsync($"/api/sources/{sourceId}/packages/Elsa.Email");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_package_ignores_malformed_default_value_json()
    {
        await using var app = new CatalogApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source);
            var feature = PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(package));
            feature.Settings[0].DefaultValueJson = "{bad";

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var package = await app.CreateClient().GetFromJsonAsync<PublicPackageResponse>($"/api/sources/{sourceId}/packages/Elsa.Email");

        package!.Versions[0].Features[0].Settings[0].DefaultValue.Should().BeNull();
    }
}
