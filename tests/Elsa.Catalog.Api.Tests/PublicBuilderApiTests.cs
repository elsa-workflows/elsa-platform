using System.Net;
using System.Net.Http.Json;
using Elsa.Catalog.Api.Public.Builder;
using Elsa.Catalog.Core.Manifests;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Testing;
using FluentAssertions;

namespace Elsa.Catalog.Api.Tests;

public sealed class PublicBuilderApiTests
{
    [Fact]
    public async Task Get_builder_catalog_returns_package_provenance_and_infrastructure()
    {
        await using var app = new CatalogApiTestApplication();
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

        catalog.Should().NotBeNull();
        var package = catalog!.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.RabbitMq").Subject;
        package.Source.Name.Should().Be("Test NuGet");
        package.Source.Url.Should().Be("https://example.test/v3/index.json");
        var feature = package.Versions.Single().Features.Should().ContainSingle(x => x.FeatureId == "rabbitmq-messaging").Subject;
        feature.Infrastructure.Should().ContainSingle(x => x.Kind == "message-broker" && x.ConfigurationKeys.Contains("RabbitMq:ConnectionString"));
        catalog.InfrastructureProviders.Should().Contain(x => x.Kind == "message-broker" && x.Provider == "rabbitmq");
    }

    [Fact]
    public async Task Get_builder_catalog_filters_by_selected_source_ids()
    {
        await using var app = new CatalogApiTestApplication();
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

        catalog!.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Selected");
        catalog.Packages.Should().NotContain(x => x.PackageId == "Elsa.Hidden");
    }

    [Fact]
    public async Task Resolve_returns_bad_request_when_packages_are_missing()
    {
        await using var app = new CatalogApiTestApplication();

        var response = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            elsaVersion = "1.0.0"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("Elsa.Email", "")]
    [InlineData(" ", "1.0.0")]
    public async Task Resolve_reports_invalid_package_selections(string? packageId, string version)
    {
        await using var app = new CatalogApiTestApplication();

        var response = await app.CreateClient().PostAsJsonAsync("/api/builder/resolve", new
        {
            packages = new[]
            {
                new { sourceId = Guid.NewGuid(), packageId, version, selectedFeatures = Array.Empty<string>() }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BuilderResolveResponse>();
        body!.Compatible.Should().BeFalse();
        body.Findings.Should().ContainSingle(x => x.Code == "package.invalidSelection");
    }

    [Fact]
    public async Task Resolve_returns_success_for_compatible_selection()
    {
        await using var app = new CatalogApiTestApplication();
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
        body!.Compatible.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_uses_source_qualified_package_version_when_package_ids_overlap()
    {
        await using var app = new CatalogApiTestApplication();
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
        body!.Compatible.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_treats_non_browseable_source_versions_as_missing()
    {
        await using var app = new CatalogApiTestApplication();
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
        body!.Compatible.Should().BeFalse();
        body.Findings.Should().ContainSingle(x => x.Code == "package.missing");
        body.Findings.Select(x => x.Code).Should().NotContain(["package.invalid", "package.notApproved", "package.suspicious"]);
    }

    [Fact]
    public async Task Resolve_reports_feature_dependency_and_conflict_failures()
    {
        await using var app = new CatalogApiTestApplication();
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
        body!.Compatible.Should().BeFalse();
        body.Findings.Should().Contain(x => x.Code == "feature.packageDependency");
        body.Findings.Should().Contain(x => x.Code == "feature.conflict");
    }
}
