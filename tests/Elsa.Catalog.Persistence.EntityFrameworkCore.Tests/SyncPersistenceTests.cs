using Elsa.Catalog.Core.Manifests;
using Elsa.Catalog.Core.Packaging;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Core.Sources;
using Elsa.Catalog.Core.Sync;
using Elsa.Catalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Elsa.Catalog.Testing;
using Elsa.PackageManifests.Validation;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Catalog.Persistence.EntityFrameworkCore.Tests;

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

        stored.Items.Should().ContainSingle(x => x.Error == "No manifest");
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

        (await db.PackageSources.CountAsync()).Should().Be(1);
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

        runs.Should().HaveCount(100);
        runs[0].Id.Should().Be(newest.Id);
        runs.Should().NotContain(x => x.StartedAt == oldest);
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

        run.Status.Should().Be(SyncRunStatus.Completed);
        (await db.SyncRunItems.CountAsync()).Should().Be(1);
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

        (await db.PackageSources.SingleAsync()).LastSyncedAt.Should().NotBeNull();
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

        result.DeletedRunCount.Should().Be(1);
        result.DeletedItemCount.Should().Be(1);
        (await db.SyncRuns.CountAsync()).Should().Be(0);
        (await db.SyncRunItems.CountAsync()).Should().Be(0);
        (await db.PackageSources.CountAsync()).Should().Be(1);
        (await db.Packages.CountAsync()).Should().Be(1);
        (await db.PackageVersions.CountAsync()).Should().Be(1);
        (await db.ManifestValidationResults.CountAsync()).Should().Be(1);
        (await db.ApprovalRecords.CountAsync()).Should().Be(1);
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

        preview.EligibleRunCount.Should().Be(2);
        preview.EligibleItemCount.Should().Be(3);
        preview.ExcludedRunCount.Should().Be(1);
        result.DeletedRunCount.Should().Be(2);
        result.DeletedItemCount.Should().Be(3);
        result.ExcludedRunCount.Should().Be(1);
        (await db.SyncRuns.Select(x => x.Id).ToListAsync()).Should().BeEquivalentTo([recent.Id, running.Id, recentRunning.Id]);
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

        result.DeletedRunCount.Should().Be(1000);
        (await db.SyncRuns.CountAsync()).Should().Be(1);
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
