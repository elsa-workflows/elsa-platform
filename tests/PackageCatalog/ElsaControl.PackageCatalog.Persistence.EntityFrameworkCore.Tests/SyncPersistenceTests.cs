using ElsaControl.PackageCatalog.Core.Manifests;
using ElsaControl.PackageCatalog.Core.Packaging;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sources;
using ElsaControl.PackageCatalog.Core.Sync;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.PackageCatalog.Testing;
using Elsa.Specifications.PackageManifests.Validation;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class SyncPersistenceTests
{
    [Fact]
    public async Task Persists_sync_run_items_for_diagnostics()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite =>
            {
                sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly);
            })
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var run = new SyncRun { Trigger = SyncRunTrigger.ManualAll };
        run.Items.Add(new SyncRunItem { SyncRun = run, SyncRunId = run.Id, PackageId = "Elsa.Email", Version = "1.0.0", Status = SyncRunItemStatus.Failed, Error = "No manifest" });
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync();

        var stored = await db.SyncRuns.Include(x => x.Items).SingleAsync();

        Assert.Single(stored.Items, x => x.Error == "No manifest");
    }

    [Fact]
    public async Task Initial_migration_creates_catalog_tables()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite =>
            {
                sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly);
            })
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.MigrateAsync();

        db.PackageSources.Add(PublicCatalogSeedData.CreatePackageSource());
        await db.SaveChangesAsync();

        Assert.Equal(1, (await db.PackageSources.CountAsync()));
    }

    [Fact]
    public async Task Lists_most_recent_sync_runs_before_limiting()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var oldest = DateTimeOffset.UtcNow.AddDays(-101);
        for (var i = 0; i < 101; i++)
            db.SyncRuns.Add(new SyncRun { Trigger = SyncRunTrigger.Scheduled, StartedAt = oldest.AddMinutes(i) });

        var newest = new SyncRun { Trigger = SyncRunTrigger.ManualAll, StartedAt = DateTimeOffset.UtcNow.AddDays(1) };
        db.SyncRuns.Add(newest);
        await db.SaveChangesAsync();

        var runs = await new SyncRunStore(db).ListAsync();

        Assert.Equal(100, runs.Count());
        Assert.Equal(newest.Id, runs[0].Id);
        Assert.DoesNotContain(runs, x => x.StartedAt == oldest);
    }

    [Fact]
    public async Task Sync_service_persists_new_run_items_without_concurrency_failure()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;
        db.PackageSources.Add(source);
        await db.SaveChangesAsync();

        var manifestJson = new ManifestFixtureBuilder()
            .WithPackage("Elsa.Email", "1.0.0")
            .WithFeature()
            .BuildJson();
        var service = new PackageSyncService(
            new PackageSourceStore(db),
            new SyncCatalogStore(db),
            new SyncRunStore(db),
            new FakeDiscovery([new DiscoveredPackageVersion("Elsa.Email", "1.0.0")]),
            new FakeDownloader(manifestJson),
            new FakeManifestReader(),
            new ManifestValidator(),
            new ManifestIngestionService(),
            new PackageVersionPolicy(),
            new NoopSyncDiagnostics(),
            new SyncConcurrencyGuard(),
            new SourceSyncActivityTracker(),
            new SyncRunCancellationRegistry());

        var run = await service.SyncAllAsync();

        Assert.Equal(SyncRunStatus.Completed, run.Status);
        Assert.Equal(1, (await db.SyncRunItems.CountAsync()));
    }

    [Fact]
    public async Task Public_catalog_projects_runtime_kinds_from_stored_manifest_json()
    {
        await using var db = await CreateOpenDbContextAsync();
        var source = PublicCatalogSeedData.CreatePackageSource();
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
        await db.SaveChangesAsync();

        var packages = await new PublicCatalogQueries(db).ListPackagesAsync([]);

        var projectedVersion = Assert.Single(Assert.Single(packages).Versions);
        Assert.Equal(new[] { "elsa.server", "acme.custom-host" }.Order(), projectedVersion.RuntimeKinds.Order());

        Assert.Equal(new[] { "elsa.server", "acme.custom-host" }.Order(), projectedVersion.Features.Single(x => x.FeatureId == "server").RuntimeKinds.Order());

        Assert.Equal("elsa.studio", Assert.Single(projectedVersion.Features.Single(x => x.FeatureId == "studio").RuntimeKinds));
    }

    [Fact]
    public async Task Bulk_sync_persists_source_last_synced_timestamp()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var source = PublicCatalogSeedData.CreatePackageSource();
        db.PackageSources.Add(source);
        await db.SaveChangesAsync();

        var service = new PackageSyncService(
            new PackageSourceStore(db),
            new SyncCatalogStore(db),
            new SyncRunStore(db),
            new FakeDiscovery([]),
            new FakeDownloader("{}"),
            new FakeManifestReader(),
            new ManifestValidator(),
            new ManifestIngestionService(),
            new PackageVersionPolicy(),
            new NoopSyncDiagnostics(),
            new SyncConcurrencyGuard(),
            new SourceSyncActivityTracker(),
            new SyncRunCancellationRegistry());

        await service.SyncAllAsync();

        Assert.NotNull((await db.PackageSources.SingleAsync()).LastSyncedAt);
    }

    [Fact]
    public async Task Delete_sync_run_removes_items_and_preserves_catalog_state()
    {
        await using var db = await CreateOpenDbContextAsync();
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source);
        var version = PublicCatalogSeedData.AddVersion(package);
        var validation = new ManifestValidationResultRecord
        {
            PackageVersion = version,
            PackageVersionId = version.Id,
            Status = ValidationStatus.Valid
        };
        var approval = new ApprovalRecord
        {
            TargetType = ApprovalTargetType.PackageVersion,
            TargetId = version.Id,
            Status = PackageApprovalStatus.Approved,
            Actor = "tester"
        };
        var run = CompletedRun(DateTimeOffset.UtcNow.AddDays(-1));
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

        db.AddRange(source, validation, approval, run);
        await db.SaveChangesAsync();

        var result = await new SyncRunStore(db).DeleteAsync(run.Id);

        Assert.Equal(1, result.DeletedRunCount);
        Assert.Equal(1, result.DeletedItemCount);
        Assert.Equal(0, (await db.SyncRuns.CountAsync()));
        Assert.Equal(0, (await db.SyncRunItems.CountAsync()));
        Assert.Equal(1, (await db.PackageSources.CountAsync()));
        Assert.Equal(1, (await db.Packages.CountAsync()));
        Assert.Equal(1, (await db.PackageVersions.CountAsync()));
        Assert.Equal(1, (await db.ManifestValidationResults.CountAsync()));
        Assert.Equal(1, (await db.ApprovalRecords.CountAsync()));
    }

    [Fact]
    public async Task Bulk_delete_removes_only_terminal_runs_before_cutoff()
    {
        await using var db = await CreateOpenDbContextAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var oldCompleted = CompletedRun(cutoff.AddDays(-1), SyncRunStatus.Completed, 2);
        var oldFailed = CompletedRun(cutoff.AddDays(-2), SyncRunStatus.Failed, 1);
        var recent = CompletedRun(cutoff.AddDays(1), SyncRunStatus.Completed, 1);
        var running = new SyncRun { Trigger = SyncRunTrigger.ManualAll, Status = SyncRunStatus.Running, StartedAt = cutoff.AddDays(-3) };
        var recentRunning = new SyncRun { Trigger = SyncRunTrigger.ManualAll, Status = SyncRunStatus.Running, StartedAt = cutoff.AddDays(1) };
        db.SyncRuns.AddRange(oldCompleted, oldFailed, recent, running, recentRunning);
        await db.SaveChangesAsync();

        var preview = await new SyncRunStore(db).PreviewDeleteBeforeAsync(cutoff, [SyncRunStatus.Completed, SyncRunStatus.CompletedWithErrors, SyncRunStatus.Failed]);
        var result = await new SyncRunStore(db).DeleteBeforeAsync(cutoff, [SyncRunStatus.Completed, SyncRunStatus.CompletedWithErrors, SyncRunStatus.Failed]);

        Assert.Equal(2, preview.EligibleRunCount);
        Assert.Equal(3, preview.EligibleItemCount);
        Assert.Equal(1, preview.ExcludedRunCount);
        Assert.Equal(2, result.DeletedRunCount);
        Assert.Equal(3, result.DeletedItemCount);
        Assert.Equal(1, result.ExcludedRunCount);
        Assert.Equal(new[] { recent.Id, running.Id, recentRunning.Id }.Order(), (await db.SyncRuns.Select(x => x.Id).ToListAsync()).Order());

    }

    [Fact]
    public async Task Bulk_delete_handles_large_historical_history()
    {
        await using var db = await CreateOpenDbContextAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var oldest = cutoff.AddDays(-10);
        for (var i = 0; i < 1000; i++)
            db.SyncRuns.Add(CompletedRun(oldest.AddMinutes(i)));

        db.SyncRuns.Add(CompletedRun(cutoff.AddMinutes(1)));
        await db.SaveChangesAsync();

        var result = await new SyncRunStore(db).DeleteBeforeAsync(cutoff, [SyncRunStatus.Completed, SyncRunStatus.CompletedWithErrors, SyncRunStatus.Failed]);

        Assert.Equal(1000, result.DeletedRunCount);
        Assert.Equal(1, (await db.SyncRuns.CountAsync()));
    }

    private static async Task<CatalogDbContext> CreateOpenDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
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

    private sealed class FakeDiscovery(IReadOnlyList<DiscoveredPackageVersion> versions) : IPackageVersionDiscoveryClient
    {
        public Task<IReadOnlyList<DiscoveredPackageVersion>> FindPackageVersionsAsync(PackageSource source, CancellationToken cancellationToken = default) => Task.FromResult(versions);
    }

    private sealed class FakeDownloader(string manifestJson) : IPackageArchiveDownloader
    {
        public Task<Stream> DownloadPackageAsync(PackageSource source, string packageId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
    }

    private sealed class FakeManifestReader : IPackageArchiveManifestReader
    {
        public async Task<PackageManifestReadResult> ReadAsync(Stream packageStream, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(packageStream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            return PackageManifestReadResult.Found("elsa-package.json", json, hash, []);
        }
    }
}
