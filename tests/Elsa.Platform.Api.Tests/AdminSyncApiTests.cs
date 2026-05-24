using System.Net;
using Elsa.Platform.Api.Admin.Sync;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Packaging;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Platform.Api.Tests;

public sealed class AdminSyncApiTests
{
    [Fact]
    public async Task Manual_sync_creates_running_sync_run_and_completes_in_background()
    {
        var discovery = new GatedDiscoveryClient();
        await using var app = new PlatformApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackageVersionDiscoveryClient>();
                services.AddSingleton<IPackageVersionDiscoveryClient>(discovery);
            });
        });

        await SeedAsync(app, db =>
        {
            db.PackageSources.Add(PublicCatalogSeedData.CreatePackageSource());
            return Task.CompletedTask;
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostAsync("/api/admin/sync", null).WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var run = await response.Content.ReadPlatformJsonAsync<AdminSyncRunResponse>();

        run!.Status.Should().Be(SyncRunStatus.Running);

        var runs = await client.GetPlatformJsonAsync<List<AdminSyncRunResponse>>("/api/admin/sync-runs");
        runs.Should().ContainSingle(x => x.Id == run.Id);

        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        discovery.Release.SetResult();
        var completed = await WaitForRunStatusAsync(client, run.Id, SyncRunStatus.Completed);
        completed.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Sync_run_list_includes_source_metadata_and_item_count()
    {
        await using var app = new PlatformApiTestApplication();
        var (runId, sourceId) = await SeedSyncRunWithSourceAsync(app);

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var runs = await client.GetPlatformJsonAsync<List<AdminSyncRunResponse>>("/api/admin/sync-runs");

        var run = runs.Should().ContainSingle(x => x.Id == runId).Subject;
        run.ItemCount.Should().Be(1);
        run.Sources.Should().ContainSingle().Which.Should().Be(new AdminSyncRunSourceResponse(sourceId, "Elsa Official"));
        run.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_run_details_include_source_metadata_and_item_count()
    {
        await using var app = new PlatformApiTestApplication();
        var (runId, sourceId) = await SeedSyncRunWithSourceAsync(app);

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var run = await client.GetPlatformJsonAsync<AdminSyncRunResponse>($"/api/admin/sync-runs/{runId}");

        run!.ItemCount.Should().Be(1);
        run.Sources.Should().ContainSingle().Which.Should().Be(new AdminSyncRunSourceResponse(sourceId, "Elsa Official"));
        run.Items.Should().ContainSingle(x => x.SourceId == sourceId && x.PackageId == "Elsa.Workflows");
    }

    [Fact]
    public async Task Manual_source_sync_response_includes_source_metadata()
    {
        await using var app = new PlatformApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackageVersionDiscoveryClient>();
                services.AddScoped<IPackageVersionDiscoveryClient, ThrowingDiscoveryClient>();
            });
        });

        var sourceId = Guid.NewGuid();
        await SeedAsync(app, db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Id = sourceId;
            source.Name = "Elsa Official";
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostAsync($"/api/admin/sync/sources/{sourceId}", null);
        var run = await response.Content.ReadPlatformJsonAsync<AdminSyncRunResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        run!.Status.Should().Be(SyncRunStatus.Running);
        run.ItemCount.Should().Be(0);
        run.Sources.Should().ContainSingle().Which.Should().Be(new AdminSyncRunSourceResponse(sourceId, "Elsa Official"));

        var completed = await WaitForRunStatusAsync(client, run.Id, SyncRunStatus.CompletedWithErrors);
        completed.ItemCount.Should().Be(1);
        completed.Sources.Should().ContainSingle().Which.Should().Be(new AdminSyncRunSourceResponse(sourceId, "Elsa Official"));
    }

    private static async Task<AdminSyncRunResponse> WaitForRunStatusAsync(HttpClient client, Guid runId, SyncRunStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                var run = await client.GetPlatformJsonAsync<AdminSyncRunResponse>($"/api/admin/sync-runs/{runId}", timeout.Token);
                if (run?.Status == status)
                    return run;

                await Task.Delay(50, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Sync run {runId} did not reach {status}.");
            }
        }
    }

    [Fact]
    public async Task Delete_sync_run_removes_history_and_preserves_catalog_state()
    {
        await using var app = new PlatformApiTestApplication();
        var runId = await SeedPackageLinkedSyncRunAsync(app);
        var client = AuthenticatedClient(app);

        var response = await client.DeleteAsync($"/api/admin/sync-runs/{runId}");
        var result = await response.Content.ReadPlatformJsonAsync<AdminSyncRunCleanupResultResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        result!.DeletedRunCount.Should().Be(1);
        result.DeletedItemCount.Should().Be(1);

        var missing = await client.GetAsync($"/api/admin/sync-runs/{runId}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.PackageSources.CountAsync()).Should().Be(1);
        (await db.Packages.CountAsync()).Should().Be(1);
        (await db.PackageVersions.CountAsync()).Should().Be(1);
        (await db.ManifestValidationResults.CountAsync()).Should().Be(1);
        (await db.ApprovalRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delete_sync_run_is_idempotent_for_missing_run()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = AuthenticatedClient(app);

        var response = await client.DeleteAsync($"/api/admin/sync-runs/{Guid.NewGuid()}");
        var result = await response.Content.ReadPlatformJsonAsync<AdminSyncRunCleanupResultResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        result!.NotFoundRunCount.Should().Be(1);
        result.DeletedRunCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_sync_run_refuses_running_run()
    {
        await using var app = new PlatformApiTestApplication();
        var runId = Guid.NewGuid();
        await app.SeedAsync(db =>
        {
            db.SyncRuns.Add(new SyncRun { Id = runId, Trigger = SyncRunTrigger.ManualAll, Status = SyncRunStatus.Running });
            return Task.CompletedTask;
        });
        var client = AuthenticatedClient(app);

        var response = await client.DeleteAsync($"/api/admin/sync-runs/{runId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Bulk_cleanup_previews_and_deletes_terminal_runs_before_cutoff()
    {
        await using var app = new PlatformApiTestApplication();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var oldCompleted = CompletedRun(cutoff.AddDays(-1), SyncRunStatus.Completed, 2);
        var oldFailed = CompletedRun(cutoff.AddDays(-2), SyncRunStatus.Failed, 1);
        var recent = CompletedRun(cutoff.AddDays(1), SyncRunStatus.Completed, 1);
        var running = new SyncRun { Trigger = SyncRunTrigger.ManualAll, Status = SyncRunStatus.Running, StartedAt = cutoff.AddDays(-3) };
        await app.SeedAsync(db =>
        {
            db.SyncRuns.AddRange(oldCompleted, oldFailed, recent, running);
            return Task.CompletedTask;
        });
        var client = AuthenticatedClient(app);

        var preview = await client.GetPlatformJsonAsync<AdminSyncRunCleanupPreviewResponse>($"/api/admin/sync-runs/deletion-preview?completedBefore={Cutoff(cutoff)}");
        var response = await client.DeleteAsync($"/api/admin/sync-runs?completedBefore={Cutoff(cutoff)}");
        var result = await response.Content.ReadPlatformJsonAsync<AdminSyncRunCleanupResultResponse>();

        preview!.EligibleRunCount.Should().Be(2);
        preview.EligibleItemCount.Should().Be(3);
        preview.ExcludedRunCount.Should().Be(1);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        result!.DeletedRunCount.Should().Be(2);
        result.DeletedItemCount.Should().Be(3);
        result.ExcludedRunCount.Should().Be(1);

        var runs = await client.GetPlatformJsonAsync<List<AdminSyncRunResponse>>("/api/admin/sync-runs");
        runs!.Select(x => x.Id).Should().BeEquivalentTo([recent.Id, running.Id]);
    }

    [Fact]
    public async Task Bulk_cleanup_rejects_future_cutoff()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(db =>
        {
            db.SyncRuns.Add(CompletedRun(DateTimeOffset.UtcNow.AddDays(-1)));
            return Task.CompletedTask;
        });
        var client = AuthenticatedClient(app);
        var futureCutoff = Cutoff(DateTimeOffset.UtcNow.AddMinutes(5));

        var preview = await client.GetAsync($"/api/admin/sync-runs/deletion-preview?completedBefore={futureCutoff}");
        var delete = await client.DeleteAsync($"/api/admin/sync-runs?completedBefore={futureCutoff}");

        preview.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetPlatformJsonAsync<List<AdminSyncRunResponse>>("/api/admin/sync-runs"))!.Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_sync_run_requires_admin_authentication()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();

        var response = await client.DeleteAsync($"/api/admin/sync-runs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Running_sync_can_be_canceled()
    {
        var discovery = new GatedDiscoveryClient();
        await using var app = new PlatformApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPackageVersionDiscoveryClient>();
                services.AddSingleton<IPackageVersionDiscoveryClient>(discovery);
            });
        });

        await SeedAsync(app, db =>
        {
            db.PackageSources.Add(PublicCatalogSeedData.CreatePackageSource());
            return Task.CompletedTask;
        });

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var running = client.PostAsync("/api/admin/sync", null);
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var activeRun = (await client.GetPlatformJsonAsync<List<AdminSyncRunResponse>>("/api/admin/sync-runs"))!.Should().ContainSingle().Subject;

        var cancelResponse = await client.PostAsync($"/api/admin/sync-runs/{activeRun.Id}/cancel", null);
        await running;
        var completedRun = await WaitForRunStatusAsync(client, activeRun.Id, SyncRunStatus.Canceled);

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK, await cancelResponse.Content.ReadAsStringAsync());
        completedRun.Status.Should().Be(SyncRunStatus.Canceled);
        completedRun.Error.Should().Be("Sync canceled by operator.");
    }

    private static async Task<(Guid RunId, Guid SourceId)> SeedSyncRunWithSourceAsync(PlatformApiTestApplication app)
    {
        var runId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            source.Id = sourceId;
            source.Name = "Elsa Official";

            var run = new SyncRun
            {
                Id = runId,
                Trigger = SyncRunTrigger.ManualSource,
                Status = SyncRunStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt = DateTimeOffset.UtcNow
            };

            run.Items.Add(new SyncRunItem
            {
                SyncRun = run,
                SyncRunId = run.Id,
                SourceId = source.Id,
                PackageId = "Elsa.Workflows",
                Version = "1.0.0",
                Status = SyncRunItemStatus.Indexed
            });

            db.PackageSources.Add(source);
            db.SyncRuns.Add(run);
            return Task.CompletedTask;
        });

        return (runId, sourceId);
    }

    private static async Task<Guid> SeedPackageLinkedSyncRunAsync(PlatformApiTestApplication app)
    {
        var runId = Guid.NewGuid();
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            var version = PublicCatalogSeedData.AddVersion(package);
            var run = CompletedRun(DateTimeOffset.UtcNow.AddDays(-1));
            run.Id = runId;
            run.Items.Add(new SyncRunItem
            {
                SyncRun = run,
                SyncRunId = run.Id,
                PackageVersion = version,
                PackageVersionId = version.Id,
                PackageId = package.PackageId,
                Version = version.Version,
                Status = SyncRunItemStatus.Indexed
            });
            db.AddRange(
                source,
                new ManifestValidationResultRecord
                {
                    PackageVersion = version,
                    PackageVersionId = version.Id,
                    Status = ValidationStatus.Valid
                },
                new ApprovalRecord
                {
                    TargetType = ApprovalTargetType.PackageVersion,
                    TargetId = version.Id,
                    Status = PackageApprovalStatus.Approved,
                    Actor = "tester"
                },
                run);
            return Task.CompletedTask;
        });

        return runId;
    }

    private static SyncRun CompletedRun(DateTimeOffset completedAt, SyncRunStatus status = SyncRunStatus.Completed, int items = 0)
    {
        var run = new SyncRun
        {
            Trigger = SyncRunTrigger.ManualAll,
            Status = status,
            StartedAt = completedAt.AddMinutes(-2),
            CompletedAt = completedAt
        };

        for (var i = 0; i < items; i++)
            run.Items.Add(new SyncRunItem { SyncRun = run, SyncRunId = run.Id, Status = SyncRunItemStatus.Indexed });

        return run;
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        return client;
    }

    private static string Cutoff(DateTimeOffset cutoff) =>
        Uri.EscapeDataString(cutoff.ToUniversalTime().ToString("O"));

    private static async Task SeedAsync(WebApplicationFactory<Program> app, Func<CatalogDbContext, Task> seed)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await seed(db);
        await db.SaveChangesAsync();
    }

    private sealed class ThrowingDiscoveryClient : IPackageVersionDiscoveryClient
    {
        public Task<IReadOnlyList<DiscoveredPackageVersion>> FindPackageVersionsAsync(PackageSource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Discovery failed.");
    }

    private sealed class GatedDiscoveryClient : IPackageVersionDiscoveryClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<DiscoveredPackageVersion>> FindPackageVersionsAsync(PackageSource source, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return [];
        }
    }
}
