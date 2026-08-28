using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Public.Packages;
using ElsaControl.Api.Public.Sources;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class PublicPackagesApiTests
{
    [Fact]
    public async Task Get_packages_returns_only_visible_packages()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Single(packages!, x => x.PackageId == "Elsa.Email");
        Assert.Single(packages!, x => x.PackageId == "Elsa.Email" && x.DisplayName == "Email");
        Assert.DoesNotContain(packages!, x => x.PackageId == "Elsa.Rejected");
    }

    [Fact]
    public async Task Get_public_sources_returns_sanitized_indexed_sources()
    {
        await using var app = new ControlApiTestApplication();
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

        var source = Assert.Single(sources!);
        Assert.Equal("https://example.test/v3/index.json", source.Url);
        Assert.Equal(1, source.PackageCount);
    }

    [Fact]
    public async Task Get_public_sources_excludes_non_browseable_sources()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Single(sources!, x => x.Name == "Public");
        Assert.DoesNotContain(sources!, x => x.Name == "Internal");
    }

    [Fact]
    public async Task Get_packages_filters_by_selected_source_ids()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Single(packages!, x => x.PackageId == "Elsa.Selected");
        Assert.DoesNotContain(packages!, x => x.PackageId == "Elsa.Hidden");
    }

    [Fact]
    public async Task Get_packages_excludes_non_browseable_sources_even_when_selected()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Empty(packages!);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
    }

    [Fact]
    public async Task Get_package_resolves_duplicate_package_id_by_source()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Equal(selectedSourceId, selected!.Source.Id);
        Assert.Single(selected.Versions, x => x.Version == "1.0.0");
        Assert.Equal(otherSourceId, other!.Source.Id);
        Assert.Single(other.Versions, x => x.Version == "2.0.0");
    }

    [Fact]
    public async Task Get_packages_hides_invalid_unlisted_rejected_and_suspicious_versions()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Single(package!.Versions, x => x.Version == "1.0.0");
    }

    [Fact]
    public async Task Get_package_returns_not_found_for_hidden_package()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_package_ignores_malformed_default_value_json()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Null(package!.Versions[0].Features[0].Settings[0].DefaultValue);
    }

    [Fact]
    public async Task Get_package_projects_runtime_kind_metadata()
    {
        await using var app = new ControlApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Mixed");
            var version = PublicCatalogSeedData.AddVersion(package);
            version.ManifestJson = new ManifestFixtureBuilder()
                .WithPackage("Elsa.Mixed", "1.0.0")
                .WithRuntimeKinds("elsa.server", "acme.custom-host")
                .WithFeature("server", "Elsa.Mixed.ServerFeature", null)
                .WithFeature("studio", "Elsa.Mixed.StudioFeature", ["elsa.studio"])
                .BuildJson();
            PublicCatalogSeedData.AddFeature(version, "server", "Server");
            PublicCatalogSeedData.AddFeature(version, "studio", "Studio");

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var package = await app.CreateClient().GetFromJsonAsync<PublicPackageResponse>($"/api/sources/{sourceId}/packages/Elsa.Mixed");

        Assert.Equivalent(new[] { "elsa.server", "acme.custom-host" }, package!.RuntimeKinds);
        Assert.Equivalent(new[] { "elsa.server", "acme.custom-host" }, package.Versions[0].RuntimeKinds);
        Assert.Equivalent(new[] { "elsa.server", "acme.custom-host" }, package.Versions[0].Features.Single(x => x.FeatureId == "server").RuntimeKinds);
        Assert.Equal(new[] { "elsa.studio" }, package.Versions[0].Features.Single(x => x.FeatureId == "studio").RuntimeKinds);
    }
}
