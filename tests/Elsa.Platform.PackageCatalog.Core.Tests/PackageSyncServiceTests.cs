using Elsa.Platform.PackageCatalog.Core.Packaging;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sources;
using Elsa.Platform.PackageCatalog.Core.Sync;
using Elsa.Platform.PackageCatalog.Core.Manifests;
using Elsa.Platform.PackageCatalog.Testing;
using Elsa.Platform.PackageManifests.Validation;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class PackageSyncServiceTests
{
    [Fact]
    public async Task Indexes_valid_manifest_and_records_sync_item()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore();
        var syncRuns = new InMemorySyncRunStore();
        var manifestJson = new ManifestFixtureBuilder().WithPackage("Elsa.Email", "1.0.0").WithFeature().BuildJson();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "1.0.0")]), new FakeDownloader(manifestJson));

        var run = await service.SyncAllAsync();

        run.Status.Should().Be(SyncRunStatus.Completed);
        run.Items.Should().ContainSingle(x => x.Status == SyncRunItemStatus.Indexed);
        catalog.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email" && x.DisplayName == "Email");
    }

    [Fact]
    public async Task Marks_changed_manifest_for_existing_version_as_suspicious()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;
        var package = PublicCatalogSeedData.CreatePackage(source);
        var version = PublicCatalogSeedData.AddVersion(package);
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore([package]);
        var syncRuns = new InMemorySyncRunStore();
        var changedManifestJson = new ManifestFixtureBuilder().WithPackage("Elsa.Email", "1.0.0").WithFeature("changed").BuildJson();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "1.0.0")]), new FakeDownloader(changedManifestJson));

        var run = await service.SyncAllAsync();

        run.Items.Should().ContainSingle(x => x.Status == SyncRunItemStatus.Suspicious);
        version.SuspiciousChangeDetected.Should().BeTrue();
    }

    [Fact]
    public async Task Updates_latest_version_when_newer_version_is_indexed()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;
        var package = PublicCatalogSeedData.CreatePackage(source);
        PublicCatalogSeedData.AddVersion(package, "1.0.0");
        package.LatestVersion = "1.0.0";
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore([package]);
        var syncRuns = new InMemorySyncRunStore();
        var manifestJson = new ManifestFixtureBuilder().WithPackage("Elsa.Email", "2.0.0").WithFeature().BuildJson();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "2.0.0")]), new FakeDownloader(manifestJson));

        var run = await service.SyncAllAsync();

        run.Status.Should().Be(SyncRunStatus.Completed);
        package.LatestVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task Uses_prefixless_display_name_when_manifest_is_invalid()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore();
        var syncRuns = new InMemorySyncRunStore();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "1.0.0")]), new FakeDownloader("{}"));

        var run = await service.SyncAllAsync();

        run.Items.Should().ContainSingle(x => x.Status == SyncRunItemStatus.Invalid);
        catalog.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email" && x.DisplayName == "Email");
    }

    [Fact]
    public async Task Uses_display_name_from_latest_valid_version_regardless_of_discovery_order()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore();
        var syncRuns = new InMemorySyncRunStore();
        var manifests = new Dictionary<string, string>
        {
            ["1.0.0"] = new ManifestFixtureBuilder().WithPackage("Elsa.Email", "1.0.0").WithDisplayName("Email Classic").WithFeature().BuildJson(),
            ["2.0.0"] = new ManifestFixtureBuilder().WithPackage("Elsa.Email", "2.0.0").WithDisplayName("Email Modern").WithFeature().BuildJson()
        };
        var service = CreateService(
            sources,
            catalog,
            syncRuns,
            new FakeDiscovery([new("Elsa.Email", "2.0.0"), new("Elsa.Email", "1.0.0")]),
            new FakeDownloader("{}", manifests));

        var run = await service.SyncAllAsync();

        run.Status.Should().Be(SyncRunStatus.Completed);
        catalog.Packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email" && x.LatestVersion == "2.0.0" && x.DisplayName == "Email Modern");
    }

    [Fact]
    public async Task Keeps_prefixless_display_name_when_old_manifest_uses_package_id()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var package = PublicCatalogSeedData.CreatePackage(source);
        var version = PublicCatalogSeedData.AddVersion(package);
        version.ManifestJson = new ManifestFixtureBuilder()
            .WithPackage("Elsa.Email", "1.0.0")
            .WithDisplayName("Elsa.Email")
            .WithFeature()
            .BuildJson();
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore([package]);
        var syncRuns = new InMemorySyncRunStore();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "2.0.0")]), new FakeDownloader("{}"));

        var run = await service.SyncAllAsync();

        run.Items.Should().ContainSingle(x => x.Status == SyncRunItemStatus.Invalid);
        package.DisplayName.Should().Be("Email");
    }

    [Fact]
    public async Task Does_not_advance_last_successful_sync_when_source_has_failed_items()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.LastSuccessfulSyncAt = DateTimeOffset.UtcNow.AddDays(-1);
        var previousLastSuccessfulSync = source.LastSuccessfulSyncAt;
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore();
        var syncRuns = new InMemorySyncRunStore();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "1.0.0")]), new ThrowingDownloader());

        var run = await service.SyncAllAsync();

        run.Status.Should().Be(SyncRunStatus.CompletedWithErrors);
        source.Status.Should().Be(PackageSourceStatus.Warning);
        source.LastSyncedAt.Should().NotBeNull();
        source.LastSuccessfulSyncAt.Should().Be(previousLastSuccessfulSync);
        source.LastSyncError.Should().Be("Elsa.Email 1.0.0: download failed");
        sources.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Clears_last_sync_error_when_source_sync_recovers()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.Status = PackageSourceStatus.Warning;
        source.LastSyncError = "Elsa.Email 1.0.0: download failed";
        var sources = new InMemorySourceStore([source]);
        var catalog = new InMemorySyncCatalogStore();
        var syncRuns = new InMemorySyncRunStore();
        var manifestJson = new ManifestFixtureBuilder().WithPackage("Elsa.Email", "1.0.0").WithFeature().BuildJson();
        var service = CreateService(sources, catalog, syncRuns, new FakeDiscovery([new("Elsa.Email", "1.0.0")]), new FakeDownloader(manifestJson));

        await service.SyncAllAsync();

        source.Status.Should().Be(PackageSourceStatus.Healthy);
        source.LastSyncError.Should().BeNull();
        source.LastSuccessfulSyncAt.Should().Be(source.LastSyncedAt);
    }

    [Fact]
    public async Task Records_source_discovery_failure_detail()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var sources = new InMemorySourceStore([source]);
        var service = CreateService(sources, new InMemorySyncCatalogStore(), new InMemorySyncRunStore(), new ThrowingDiscovery(), new FakeDownloader("{}"));

        var run = await service.SyncAllAsync();

        run.Status.Should().Be(SyncRunStatus.CompletedWithErrors);
        source.Status.Should().Be(PackageSourceStatus.Error);
        source.LastSyncError.Should().Be("feed unreachable");
    }

    [Fact]
    public async Task Rejects_overlapping_all_source_syncs()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var discovery = new GatedDiscovery();
        var service = CreateService(new InMemorySourceStore([source]), new InMemorySyncCatalogStore(), new InMemorySyncRunStore(), discovery, new FakeDownloader("{}"));

        var running = service.SyncAllAsync();
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rejected = await service.SyncAllAsync();
        discovery.Release.SetResult();
        var completed = await running;

        rejected.Status.Should().Be(SyncRunStatus.Failed);
        rejected.Error.Should().Contain("already active");
        completed.Status.Should().Be(SyncRunStatus.Completed);
    }

    [Fact]
    public async Task Manual_start_persists_running_run_and_holds_concurrency_until_work_item_completes()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var discovery = new GatedDiscovery();
        var syncRuns = new InMemorySyncRunStore();
        var service = CreateService(new InMemorySourceStore([source]), new InMemorySyncCatalogStore(), syncRuns, discovery, new FakeDownloader("{}"));

        var started = await service.StartManualAllAsync();

        started.Accepted.Should().BeTrue();
        started.Run.Status.Should().Be(SyncRunStatus.Running);
        syncRuns.Runs.Should().ContainSingle(x => x.Id == started.Run.Id && x.Status == SyncRunStatus.Running);
        discovery.Started.Task.IsCompleted.Should().BeFalse();

        var rejected = await service.StartManualAllAsync();
        rejected.Accepted.Should().BeFalse();
        rejected.Run.Status.Should().Be(SyncRunStatus.Failed);
        rejected.Run.Error.Should().Contain("already active");

        var executing = service.ExecuteManualWorkItemAsync(started.WorkItem!);
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        discovery.Release.SetResult();
        await executing;

        started.Run.Status.Should().Be(SyncRunStatus.Completed);
    }

    [Fact]
    public async Task Manual_work_item_failure_marking_completes_run_and_releases_concurrency()
    {
        var syncRuns = new InMemorySyncRunStore();
        var service = CreateService(new InMemorySourceStore([]), new InMemorySyncCatalogStore(), syncRuns, new FakeDiscovery([]), new FakeDownloader("{}"));
        var started = await service.StartManualAllAsync();

        await service.MarkManualWorkItemFailedAsync(started.WorkItem!, "database unavailable");
        started.WorkItem!.Dispose();

        started.Run.Status.Should().Be(SyncRunStatus.Failed);
        started.Run.Error.Should().Be("database unavailable");
        started.Run.CompletedAt.Should().NotBeNull();

        var next = await service.StartManualAllAsync();
        next.Accepted.Should().BeTrue();
        next.WorkItem!.Dispose();
    }

    [Fact]
    public async Task Tracks_source_sync_activity_while_source_is_running()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var discovery = new GatedDiscovery();
        var syncActivity = new SourceSyncActivityTracker();
        var service = CreateService(new InMemorySourceStore([source]), new InMemorySyncCatalogStore(), new InMemorySyncRunStore(), discovery, new FakeDownloader("{}"), syncActivity);

        var running = service.SyncAllAsync();
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        syncActivity.IsSourceSyncing(source.Id).Should().BeTrue();

        discovery.Release.SetResult();
        await running;

        syncActivity.IsSourceSyncing(source.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Cancels_running_sync_when_requested()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        var discovery = new GatedDiscovery();
        var syncRuns = new InMemorySyncRunStore();
        var cancellationRegistry = new SyncRunCancellationRegistry();
        var service = CreateService(
            new InMemorySourceStore([source]),
            new InMemorySyncCatalogStore(),
            syncRuns,
            discovery,
            new FakeDownloader("{}"),
            cancellationRegistry: cancellationRegistry);

        var running = service.SyncAllAsync();
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellationRegistry.Cancel(syncRuns.Runs.Single().Id).Should().BeTrue();
        var run = await running;

        run.Status.Should().Be(SyncRunStatus.Canceled);
        run.Error.Should().Be("Sync canceled by operator.");
    }

    private static PackageSyncService CreateService(
        IPackageSourceStore sources,
        ISyncCatalogStore catalog,
        ISyncRunStore syncRuns,
        IPackageVersionDiscoveryClient discovery,
        IPackageArchiveDownloader downloader,
        SourceSyncActivityTracker? syncActivity = null,
        SyncRunCancellationRegistry? cancellationRegistry = null) =>
        new(
            sources,
            catalog,
            syncRuns,
            discovery,
            downloader,
            new FakeManifestReader(),
            new ManifestValidator(),
            new ManifestIngestionService(),
            new PackageVersionPolicy(),
            new NoopSyncDiagnostics(),
            new SyncConcurrencyGuard(),
            syncActivity ?? new SourceSyncActivityTracker(),
            cancellationRegistry ?? new SyncRunCancellationRegistry());

    private sealed class InMemorySourceStore(IReadOnlyList<PackageSource> sources) : IPackageSourceStore
    {
        public int SaveChangesCount { get; private set; }

        public Task<IReadOnlyList<PackageSource>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(sources);
        public Task<PackageSource?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(sources.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());
        public Task AddAsync(PackageSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySyncCatalogStore(IReadOnlyList<Package>? packages = null) : ISyncCatalogStore
    {
        public List<Package> Packages { get; } = packages?.ToList() ?? [];

        public Task<Package?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.SingleOrDefault(x => x.SourceId == sourceId && x.PackageId == packageId));

        public Task<PackageVersion?> GetPackageVersionAsync(Guid packageId, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.SelectMany(x => x.Versions).SingleOrDefault(x => x.PackageId == packageId && x.Version == version));

        public Task AddPackageAsync(Package package, CancellationToken cancellationToken = default)
        {
            Packages.Add(package);
            return Task.CompletedTask;
        }

        public Task AddValidationResultAsync(ManifestValidationResultRecord result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemorySyncRunStore : ISyncRunStore
    {
        public List<SyncRun> Runs { get; } = [];
        public List<SyncRunItem> Items { get; } = [];
        public Task<IReadOnlyList<SyncRun>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SyncRun>>(Runs);
        public Task<SyncRun?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Runs.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyDictionary<Guid, SyncRunListMetadata>> GetListMetadataAsync(IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken = default)
        {
            var itemMetadata = Items
                .Where(x => runIds.Contains(x.SyncRunId))
                .GroupBy(x => x.SyncRunId)
                .ToDictionary(
                    x => x.Key,
                    x => new SyncRunListMetadata(
                        x.Count(),
                        x.Where(item => item.SourceId.HasValue)
                            .Select(item => item.SourceId!.Value)
                            .Distinct()
                            .OrderBy(sourceId => sourceId.ToString())
                            .Select(sourceId => new SyncRunSourceReference(sourceId, null))
                            .ToList()));

            var metadata = runIds
                .Distinct()
                .ToDictionary(
                    x => x,
                    x => itemMetadata.GetValueOrDefault(x) ?? new SyncRunListMetadata(0, []));

            return Task.FromResult<IReadOnlyDictionary<Guid, SyncRunListMetadata>>(metadata);
        }

        public Task<SyncRunDeletionCandidate?> GetDeletionCandidateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var run = Runs.SingleOrDefault(x => x.Id == id);
            return Task.FromResult(run is null ? null : new SyncRunDeletionCandidate(run.Id, run.Status, Items.Count(x => x.SyncRunId == run.Id)));
        }

        public Task<SyncRunCleanupPreview> PreviewDeleteBeforeAsync(DateTimeOffset completedBefore, IReadOnlyCollection<SyncRunStatus> terminalStatuses, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncRunCleanupPreview(completedBefore, 0, 0, 0, null, null));

        public Task<SyncRunCleanupResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncRunCleanupResult(0, 0, 0, 0, null, []));

        public Task<SyncRunCleanupResult> DeleteBeforeAsync(DateTimeOffset completedBefore, IReadOnlyCollection<SyncRunStatus> terminalStatuses, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncRunCleanupResult(0, 0, 0, 0, completedBefore, []));

        public Task AddAsync(SyncRun run, CancellationToken cancellationToken = default)
        {
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task AddItemAsync(SyncRunItem item, CancellationToken cancellationToken = default)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDiscovery(IReadOnlyList<DiscoveredPackageVersion> versions) : IPackageVersionDiscoveryClient
    {
        public Task<IReadOnlyList<DiscoveredPackageVersion>> FindPackageVersionsAsync(PackageSource source, CancellationToken cancellationToken = default) => Task.FromResult(versions);
    }

    private sealed class ThrowingDiscovery : IPackageVersionDiscoveryClient
    {
        public Task<IReadOnlyList<DiscoveredPackageVersion>> FindPackageVersionsAsync(PackageSource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("feed unreachable");
    }

    private sealed class GatedDiscovery : IPackageVersionDiscoveryClient
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

    private sealed class FakeDownloader(string manifestJson, IReadOnlyDictionary<string, string>? manifestsByVersion = null) : IPackageArchiveDownloader
    {
        public Task<Stream> DownloadPackageAsync(PackageSource source, string packageId, string version, CancellationToken cancellationToken = default)
        {
            var json = manifestsByVersion?.GetValueOrDefault(version) ?? manifestJson;
            return Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        }
    }

    private sealed class ThrowingDownloader : IPackageArchiveDownloader
    {
        public Task<Stream> DownloadPackageAsync(PackageSource source, string packageId, string version, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("download failed");
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
