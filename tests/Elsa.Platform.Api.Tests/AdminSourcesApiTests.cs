using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Admin.Sources;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class AdminSourcesApiTests
{
    [Fact]
    public async Task Admin_sources_require_api_key()
    {
        await using var app = new PlatformApiTestApplication();

        var response = await app.CreateClient().GetAsync("/api/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Can_create_and_list_source()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var create = await client.PostPlatformJsonAsync("/api/admin/sources", new AdminSourceRequest(
            "NuGet",
            "https://example.test/v3/index.json",
            true,
            ["Elsa.*"],
            ["Elsa.Experimental.*"],
            PackageSourceApprovalPolicy.Manual,
            PackageSourceVersionDiscoveryPolicy.LatestPreview));

        create.StatusCode.Should().Be(HttpStatusCode.OK);

        var sources = await client.GetPlatformJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        sources.Should().ContainSingle(x =>
            x.Name == "NuGet" &&
            x.IncludePatterns.Contains("Elsa.*") &&
            x.ExcludePatterns.Contains("Elsa.Experimental.*") &&
            x.VersionDiscoveryPolicy == PackageSourceVersionDiscoveryPolicy.LatestPreview);
    }

    [Fact]
    public async Task Can_create_source_from_browser_json_contract()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        const string request = """
            {
              "name": "NuGet",
              "url": "https://example.test/v3/index.json",
              "enabled": true,
              "approvalPolicy": "AutoApprove",
              "versionDiscoveryPolicy": "LatestPreview",
              "includePatterns": ["Elsa.*"],
              "excludePatterns": [],
              "pollingInterval": "PT30M"
            }
            """;

        var response = await client.PostAsync("/api/admin/sources", new StringContent(request, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.RootElement.GetProperty("approvalPolicy").GetString().Should().Be("AutoApprove");
        json.RootElement.GetProperty("versionDiscoveryPolicy").GetString().Should().Be("LatestPreview");
        json.RootElement.GetProperty("type").GetString().Should().Be("NuGetFeed");
    }

    [Fact]
    public async Task Lists_source_health_last_successful_sync_and_package_count()
    {
        await using var app = new PlatformApiTestApplication();
        var lastSuccessfulSync = DateTimeOffset.UtcNow.AddMinutes(-15);
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Status = PackageSourceStatus.Warning;
            source.LastSyncedAt = DateTimeOffset.UtcNow;
            source.LastSuccessfulSyncAt = lastSuccessfulSync;
            source.LastSyncError = "Elsa.Email 1.0.0: download failed";
            source.PollingInterval = "PT30M";
            source.VersionDiscoveryPolicy = PackageSourceVersionDiscoveryPolicy.LatestStable;
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var sources = await client.GetPlatformJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        var source = sources.Should().ContainSingle().Subject;
        source.Status.Should().Be(PackageSourceStatus.Warning);
        source.LastSuccessfulSyncAt.Should().BeCloseTo(lastSuccessfulSync, TimeSpan.FromSeconds(1));
        source.LastSyncError.Should().Be("Elsa.Email 1.0.0: download failed");
        source.PackageCount.Should().Be(1);
        source.PollingInterval.Should().Be("PT30M");
        source.VersionDiscoveryPolicy.Should().Be(PackageSourceVersionDiscoveryPolicy.LatestStable);
    }

    [Fact]
    public async Task Lists_source_sync_activity()
    {
        await using var app = new PlatformApiTestApplication();
        var sourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            sourceId = source.Id;
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        var syncActivity = app.Services.GetRequiredService<SourceSyncActivityTracker>();

        using var activity = syncActivity.BeginSourceSync(sourceId);

        var sources = await client.GetPlatformJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        sources.Should().ContainSingle().Subject.IsSyncing.Should().BeTrue();
    }

    [Fact]
    public async Task Soft_deleted_sources_are_hidden_from_admin_reads()
    {
        await using var app = new PlatformApiTestApplication();
        var deletedSourceId = Guid.Empty;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.SoftDeletedAt = DateTimeOffset.UtcNow;
            deletedSourceId = source.Id;
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var sources = await client.GetPlatformJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");
        var getDeleted = await client.GetAsync($"/api/admin/sources/{deletedSourceId}");

        sources.Should().BeEmpty();
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_source_returns_bad_request()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostPlatformJsonAsync("/api/admin/sources", new AdminSourceRequest(
            "NuGet",
            "not-a-url",
            true,
            [],
            [],
            PackageSourceApprovalPolicy.Manual));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
