using ValenceControl.PackageCatalog.Abstractions.Catalog;
using ValenceControl.PackageCatalog.Core.Packages;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace ValenceControl.PackageCatalog.Core.Tests;

public sealed class PublicCatalogQueryServiceTests
{
    [Fact]
    public async Task Delegates_public_catalog_reads_to_query_port()
    {
        var queries = new CapturingPublicCatalogQueries();
        var service = new PublicCatalogQueryService(queries, CreateCache());
        var sourceId = Guid.NewGuid();

        var packages = await service.ListPackagesAsync([sourceId]);

        packages.Should().ContainSingle(x => x.PackageId == "Elsa.Email");
        queries.ListPackagesCalled.Should().BeTrue();
        queries.ListPackageSourceIds.Should().ContainSingle().Which.Should().Be(sourceId);
    }

    [Fact]
    public async Task Caches_public_catalog_reads_until_invalidated()
    {
        var queries = new CapturingPublicCatalogQueries();
        var cache = CreateCache();
        var service = new PublicCatalogQueryService(queries, cache);

        await service.ListPackagesAsync();
        await service.ListPackagesAsync();
        cache.Invalidate();
        await service.ListPackagesAsync();

        queries.ListPackagesCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Serializes_concurrent_cache_misses_for_the_same_key()
    {
        var queries = new CapturingPublicCatalogQueries();
        var releaseQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.ListPackagesDelay = releaseQuery.Task;
        var service = new PublicCatalogQueryService(queries, CreateCache());

        var requests = Enumerable.Range(0, 10).Select(_ => service.ListPackagesAsync()).ToList();
        await WaitForAsync(() => queries.ListPackagesCallCount == 1);
        queries.ListPackagesCallCount.Should().Be(1);

        releaseQuery.SetResult();
        await Task.WhenAll(requests);

        queries.ListPackagesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Canceled_waiter_does_not_release_or_remove_unacquired_key_lock()
    {
        var cache = CreateCache();
        var factoryCalls = 0;
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetOrCreateAsync("packages:list", async _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            await releaseFactory.Task;
            return 1;
        });
        await factoryStarted.Task;

        using var canceledWait = new CancellationTokenSource();
        var waiting = cache.GetOrCreateAsync("packages:list", _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(2);
        }, canceledWait.Token);
        canceledWait.Cancel();

        var canceledAct = async () => await waiting;
        await canceledAct.Should().ThrowAsync<OperationCanceledException>();

        var next = cache.GetOrCreateAsync("packages:list", _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(3);
        });
        factoryCalls.Should().Be(1);

        releaseFactory.SetResult();
        (await first).Should().Be(1);
        (await next).Should().Be(1);
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task Uses_distinct_cache_entries_for_different_source_filters()
    {
        var queries = new CapturingPublicCatalogQueries();
        var service = new PublicCatalogQueryService(queries, CreateCache());

        await service.ListPackagesAsync([Guid.Parse("00000000-0000-0000-0000-000000000001")]);
        await service.ListPackagesAsync([Guid.Parse("00000000-0000-0000-0000-000000000002")]);

        queries.ListPackagesCallCount.Should().Be(2);
    }

    private static PublicCatalogCache CreateCache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }

    private sealed class CapturingPublicCatalogQueries : IPublicCatalogQueries
    {
        public bool ListPackagesCalled { get; private set; }
        public int ListPackagesCallCount { get; private set; }
        public IReadOnlyList<Guid> ListPackageSourceIds { get; private set; } = [];
        public Task? ListPackagesDelay { get; set; }

        public async Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default)
        {
            ListPackagesCalled = true;
            ListPackagesCallCount++;
            ListPackageSourceIds = sourceIds;
            if (ListPackagesDelay is not null)
                await ListPackagesDelay;

            return [new PublicPackageProjection("Elsa.Email", "Email", new PublicPackageSourceProjection(Guid.NewGuid(), "Test NuGet", "https://example.test/v3/index.json"), ["elsa.server"], "1.0.0", [])];
        }

        public Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) => Task.FromResult<PublicPackageProjection?>(null);
        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PublicPackageProjection>>([]);
        public Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) => Task.FromResult<PublicPackageProjection?>(null);
        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PublicPackageVersionProjection>>([]);
        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PublicPackageVersionProjection>>([]);
        public Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) => Task.FromResult<PublicPackageVersionProjection?>(null);
        public Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) => Task.FromResult<PublicPackageVersionProjection?>(null);
        public Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PublicFeatureProjection>>([]);
        public Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default) => Task.FromResult<PublicFeatureProjection?>(null);
    }
}
