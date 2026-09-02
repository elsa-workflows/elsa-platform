using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Public.Builder;
using ElsaControl.PackageCatalog.Core.Manifests;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.Api.Tests;

public sealed class PublicBuilderApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public PublicBuilderApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Get_builder_catalog_returns_package_provenance_and_infrastructure()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Url = "https://example.test/v3/index.json?token=secret";
            var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.RabbitMq");
            var version = PublicCatalogSeedData.AddVersion(package);
            PublicCatalogSeedData.AddFeature(version, "rabbitmq-messaging", "RabbitMQ Messaging");
            version.Features[0].InfrastructureJson = """
            [
              {
                "id": "message-broker",
                "kind": "message-broker",
                "providers": ["rabbitmq"],
                "configurationKeys": ["RabbitMq:ConnectionString"]
              }
            ]
            """;

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var catalog = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog");

        Assert.NotNull(catalog);
        var package = Assert.Single(catalog!.Packages, x => x.PackageId == "Elsa.RabbitMq");
        Assert.Equal("Test NuGet", package.Source.Name);
        Assert.Equal("https://example.test/v3/index.json", package.Source.Url);
        var feature = Assert.Single(package.Versions.Single().Features, x => x.FeatureId == "rabbitmq-messaging");
        Assert.Single(feature.Infrastructure, x => x.Kind == "message-broker" && x.ConfigurationKeys.Contains("RabbitMq:ConnectionString"));
        Assert.Contains(catalog.InfrastructureProviders, x => x.Kind == "message-broker" && x.Provider == "rabbitmq");
    }

    [Fact]
    public async Task Reused_host_reset_replaces_database_and_catalog_cache()
    {
        var app = _app;

        async Task SeedPackageAsync(string packageId)
        {
            await app.SeedAsync(db =>
            {
                var source = PublicCatalogSeedData.CreatePackageSource();
                PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(source, packageId));
                db.PackageSources.Add(source);
                return Task.CompletedTask;
            });
        }

        await SeedPackageAsync("Elsa.BeforeReset");
        var before = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog");
        Assert.NotNull(before);
        Assert.Contains(before.Packages, x => x.PackageId == "Elsa.BeforeReset");

        await SeedPackageAsync("Elsa.AfterReset");
        var after = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog");
        Assert.NotNull(after);
        Assert.Contains(after.Packages, x => x.PackageId == "Elsa.AfterReset");
        Assert.DoesNotContain(after.Packages, x => x.PackageId == "Elsa.BeforeReset");
    }

    [Fact]
    public async Task Get_builder_catalog_returns_image_and_feature_runtime_kinds()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.RuntimeKinds");
            var version = PublicCatalogSeedData.AddVersion(package);
            PublicCatalogSeedData.AddFeature(version, "server-default", "Server Default");
            PublicCatalogSeedData.AddFeature(version, "studio-override", "Studio Override");
            version.ManifestJson = """
            {
              "schemaVersion": "1.0",
              "package": { "id": "Elsa.RuntimeKinds", "version": "1.0.0" },
              "displayName": "Runtime Kinds",
              "compatibility": { "runtimeKinds": ["elsa.server"] },
              "features": [
                { "id": "server-default", "typeName": "Elsa.ServerDefaultFeature", "displayName": "Server Default" },
                {
                  "id": "studio-override",
                  "typeName": "Elsa.StudioOverrideFeature",
                  "displayName": "Studio Override",
                  "compatibility": { "runtimeKinds": ["elsa.studio"] }
                }
              ]
            }
            """;

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var catalog = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog");

        Assert.Equal(new[] { "elsa.server" }, catalog!.Images.Single(x => x.Slug == "elsa-pro-server").RuntimeKinds);
        Assert.Equal(new[] { "elsa.studio" }, catalog.Images.Single(x => x.Slug == "elsa-pro-studio").RuntimeKinds);
        Assert.Equal(new[] { "elsa.server", "elsa.studio" }.Order(), catalog.Images.Single(x => x.Slug == "elsa-pro-combined").RuntimeKinds.Order());
        var features = catalog.Packages.Single(x => x.PackageId == "Elsa.RuntimeKinds").Versions.Single().Features;
        Assert.Equal(new[] { "elsa.server" }, features.Single(x => x.FeatureId == "server-default").RuntimeKinds);
        Assert.Equal(new[] { "elsa.studio" }, features.Single(x => x.FeatureId == "studio-override").RuntimeKinds);
    }

    [Fact]
    public async Task Get_builder_catalog_filters_by_selected_source_ids()
    {
        var app = _app;
        var selectedSourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var selectedSource = PublicCatalogSeedData.CreatePackageSource();
            selectedSourceId = selectedSource.Id;
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(selectedSource, "Elsa.Selected"));

            var hiddenSource = PublicCatalogSeedData.CreatePackageSource();
            PublicCatalogSeedData.AddVersion(PublicCatalogSeedData.CreatePackage(hiddenSource, "Elsa.Hidden"));

            db.PackageSources.AddRange(selectedSource, hiddenSource);
            return Task.CompletedTask;
        });

        var catalog = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>($"/api/builder/catalog?sourceIds={selectedSourceId}");

        Assert.Single(catalog!.Packages, x => x.PackageId == "Elsa.Selected");
        Assert.DoesNotContain(catalog.Packages, x => x.PackageId == "Elsa.Hidden");
    }

    [Fact]
    public async Task Get_builder_catalog_returns_all_runtime_kind_features_for_client_side_image_filtering()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();

            var serverPackage = PublicCatalogSeedData.CreatePackage(source, "Elsa.ServerOnly");
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(serverPackage, runtimeKinds: ["elsa.server"]), "server", "Server");

            var studioPackage = PublicCatalogSeedData.CreatePackage(source, "Elsa.StudioOnly");
            PublicCatalogSeedData.AddFeature(PublicCatalogSeedData.AddVersion(studioPackage, runtimeKinds: ["elsa.studio"]), "studio", "Studio");

            var mixedPackage = PublicCatalogSeedData.CreatePackage(source, "Elsa.Mixed");
            var mixedVersion = PublicCatalogSeedData.AddVersion(mixedPackage);
            mixedVersion.ManifestJson = new ManifestFixtureBuilder()
                .WithPackage("Elsa.Mixed", "1.0.0")
                .WithRuntimeKinds("elsa.server", "elsa.studio")
                .WithFeature("server", "Elsa.Mixed.ServerFeature", ["elsa.server"])
                .WithFeature("studio", "Elsa.Mixed.StudioFeature", ["elsa.studio"])
                .BuildJson();
            PublicCatalogSeedData.AddFeature(mixedVersion, "server", "Server");
            PublicCatalogSeedData.AddFeature(mixedVersion, "studio", "Studio");

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var catalog = await app.CreateClient().GetFromJsonAsync<BuilderCatalogResponse>("/api/builder/catalog");

        Assert.Contains(catalog!.Packages, x => x.PackageId == "Elsa.ServerOnly");
        Assert.Contains(catalog.Packages, x => x.PackageId == "Elsa.Mixed");
        Assert.Contains(catalog.Packages, x => x.PackageId == "Elsa.StudioOnly");
        Assert.Equal(new[] { "server", "studio" }.Order(), catalog.Packages.Single(x => x.PackageId == "Elsa.Mixed").Versions.Single().Features.Select(x => x.FeatureId).Order());
    }

    [Fact]
    public async Task Resolve_returns_bad_request_when_packages_are_missing()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            elsaVersion = "1.0.0"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("Elsa.Email", "")]
    [InlineData(" ", "1.0.0")]
    public async Task Resolve_reports_invalid_package_selections(string? packageId, string version)
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            packages = new[]
            {
                new { sourceId = Guid.NewGuid(), packageId, version, selectedFeatures = Array.Empty<string>() }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BuilderResolveResponse>();
        Assert.False(body!.Compatible);
        Assert.Single(body.Findings, x => x.Code == "package.invalidSelection");
    }

    [Fact]
    public async Task Resolve_returns_success_for_compatible_selection()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Email");
            var version = PublicCatalogSeedData.AddVersion(package);
            version.ManifestJson = """
            {
              "schemaVersion": "1.0",
              "package": { "id": "Elsa.Email", "version": "1.0.0" },
              "displayName": "Email",
              "features": [
                { "id": "email", "typeName": "Elsa.Email.EmailFeature", "displayName": "Email" }
              ]
            }
            """;
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var result = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            packages = new[]
            {
                new { sourceId, packageId = "Elsa.Email", version = "1.0.0", selectedFeatures = new[] { "email" } }
            }
        });

        var body = await result.Content.ReadFromJsonAsync<BuilderResolveResponse>();
        Assert.True(body!.Compatible);
    }

    [Fact]
    public async Task Resolve_uses_source_qualified_package_version_when_package_ids_overlap()
    {
        var app = _app;
        var compatibleSourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var compatibleSource = PublicCatalogSeedData.CreatePackageSource();
            compatibleSourceId = compatibleSource.Id;
            var compatiblePackage = PublicCatalogSeedData.CreatePackage(compatibleSource, "Elsa.Email");
            PublicCatalogSeedData.AddVersion(compatiblePackage).ManifestJson = """
            {
              "schemaVersion": "1.0",
              "package": { "id": "Elsa.Email", "version": "1.0.0" },
              "displayName": "Email",
              "compatibility": { "elsaVersionRange": "[1.0.0,2.0.0)" }
            }
            """;

            var incompatibleSource = PublicCatalogSeedData.CreatePackageSource();
            var incompatiblePackage = PublicCatalogSeedData.CreatePackage(incompatibleSource, "Elsa.Email");
            PublicCatalogSeedData.AddVersion(incompatiblePackage).ManifestJson = """
            {
              "schemaVersion": "1.0",
              "package": { "id": "Elsa.Email", "version": "1.0.0" },
              "displayName": "Email",
              "compatibility": { "elsaVersionRange": "[3.0.0,4.0.0)" }
            }
            """;

            db.PackageSources.AddRange(compatibleSource, incompatibleSource);
            return Task.CompletedTask;
        });

        var result = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            elsaVersion = "1.0.0",
            packages = new[]
            {
                new { sourceId = compatibleSourceId, packageId = "Elsa.Email", version = "1.0.0", selectedFeatures = Array.Empty<string>() }
            }
        });

        var body = await result.Content.ReadFromJsonAsync<BuilderResolveResponse>();
        Assert.True(body!.Compatible);
    }

    [Fact]
    public async Task Resolve_treats_non_browseable_source_versions_as_missing()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Browseable = false;
            sourceId = source.Id;
            var package = PublicCatalogSeedData.CreatePackage(source, "Elsa.Email");
            PublicCatalogSeedData.AddVersion(package, validationStatus: ValidationStatus.Invalid, approvalStatus: PackageApprovalStatus.Rejected, suspicious: true);

            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var result = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            packages = new[]
            {
                new { sourceId, packageId = "Elsa.Email", version = "1.0.0", selectedFeatures = Array.Empty<string>() }
            }
        });

        var body = await result.Content.ReadFromJsonAsync<BuilderResolveResponse>();
        Assert.False(body!.Compatible);
        Assert.Single(body.Findings, x => x.Code == "package.missing");
        var findingCodes = body.Findings.Select(x => x.Code);
        Assert.DoesNotContain("package.invalid", findingCodes);
        Assert.DoesNotContain("package.notApproved", findingCodes);
        Assert.DoesNotContain("package.suspicious", findingCodes);
    }

    [Fact]
    public async Task Resolve_reports_feature_dependency_and_conflict_failures()
    {
        var app = _app;
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
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
                  "dependencies": [{ "packageId": "Elsa.Smtp" }],
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
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var response = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            packages = new[]
            {
                new { sourceId, packageId = "Elsa.Email", version = "1.0.0", selectedFeatures = new[] { "email" } },
                new { sourceId, packageId = "Elsa.Sms", version = "1.0.0", selectedFeatures = new[] { "sms" } }
            }
        });

        var body = await response.Content.ReadFromJsonAsync<BuilderResolveResponse>();
        Assert.False(body!.Compatible);
        Assert.Contains(body.Findings, x => x.Code == "feature.packageDependency");
        Assert.Contains(body.Findings, x => x.Code == "feature.conflict");
    }
}
