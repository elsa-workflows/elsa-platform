using System.Net;
using System.Text;
using System.Text.Json;
using ValenceControl.Api.Admin.Sources;
using ValenceControl.Api.Authentication;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageCatalog.Core.Sync;
using ValenceControl.PackageCatalog.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class AdminSourcesApiTests
{
    [Fact]
    public async Task Admin_sources_require_api_key()
    {
        await using var app = new ControlApiTestApplication();

        var response = await app.CreateClient().GetAsync("/api/admin/sources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Can_create_and_list_source()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var create = await client.PostControlJsonAsync("/api/admin/sources", new AdminSourceRequest(
            "NuGet",
            "https://example.test/v3/index.json",
            true,
            ["Elsa.*"],
            ["Elsa.Experimental.*"],
            PackageSourceApprovalPolicy.Manual,
            PackageSourceVersionDiscoveryPolicy.LatestPreview));

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var sources = await client.GetControlJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        Assert.NotNull(sources);
        Assert.Single(sources!, x =>
            x.Name == "NuGet" &&
            x.IncludePatterns.Contains("Elsa.*") &&
            x.ExcludePatterns.Contains("Elsa.Experimental.*") &&
            x.VersionDiscoveryPolicy == PackageSourceVersionDiscoveryPolicy.LatestPreview);
    }

    [Fact]
    public async Task Can_create_source_from_browser_json_contract()
    {
        await using var app = new ControlApiTestApplication();
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AutoApprove", json.RootElement.GetProperty("approvalPolicy").GetString());
        Assert.Equal("LatestPreview", json.RootElement.GetProperty("versionDiscoveryPolicy").GetString());
        Assert.Equal("NuGetFeed", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Lists_source_health_last_successful_sync_and_package_count()
    {
        await using var app = new ControlApiTestApplication();
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

        var sources = await client.GetControlJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        Assert.NotNull(sources);
        var source = Assert.Single(sources!);
        Assert.Equal(PackageSourceStatus.Warning, source.Status);
        Assert.NotNull(source.LastSuccessfulSyncAt);
        Assert.InRange(source.LastSuccessfulSyncAt.Value, lastSuccessfulSync - TimeSpan.FromSeconds(1), lastSuccessfulSync + TimeSpan.FromSeconds(1));
        Assert.Equal("Elsa.Email 1.0.0: download failed", source.LastSyncError);
        Assert.Equal(1, source.PackageCount);
        Assert.Equal("PT30M", source.PollingInterval);
        Assert.Equal(PackageSourceVersionDiscoveryPolicy.LatestStable, source.VersionDiscoveryPolicy);
    }

    [Fact]
    public async Task Lists_source_sync_activity()
    {
        await using var app = new ControlApiTestApplication();
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

        var sources = await client.GetControlJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");

        Assert.NotNull(sources);
        Assert.True(Assert.Single(sources!).IsSyncing);
    }

    [Fact]
    public async Task Soft_deleted_sources_are_hidden_from_admin_reads()
    {
        await using var app = new ControlApiTestApplication();
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

        var sources = await client.GetControlJsonAsync<List<AdminSourceResponse>>("/api/admin/sources");
        var getDeleted = await client.GetAsync($"/api/admin/sources/{deletedSourceId}");

        Assert.Empty(sources!);
        Assert.Equal(HttpStatusCode.NotFound, getDeleted.StatusCode);
    }

    [Fact]
    public async Task Invalid_source_returns_bad_request()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostControlJsonAsync("/api/admin/sources", new AdminSourceRequest(
            "NuGet",
            "not-a-url",
            true,
            [],
            [],
            PackageSourceApprovalPolicy.Manual));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
