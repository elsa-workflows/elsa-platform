using System.Collections.Concurrent;
using Elsa.Platform.PackageCatalog.Core.Manifests;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Elsa.Platform.PackageCatalog.Core.Packages;

public sealed class PublicCatalogQueryService(IPublicCatalogQueries queries, PublicCatalogCache cache)
{
    public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyCollection<Guid>? sourceIds = null, CancellationToken cancellationToken = default)
    {
        var normalizedSourceIds = NormalizeSourceIds(sourceIds);
        return cache.GetOrCreateAsync($"packages:list:{CreateSourceCacheKey(normalizedSourceIds)}", token => queries.ListPackagesAsync(normalizedSourceIds, token), cancellationToken);
    }

    public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyCollection<Guid>? sourceIds = null, CancellationToken cancellationToken = default)
    {
        var normalizedSourceIds = NormalizeSourceIds(sourceIds);
        return queries.ListPackagesForWorkspaceAsync(workspaceId, normalizedSourceIds, cancellationToken);
    }

    public Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync($"packages:item:{sourceId:N}:{packageId}", token => queries.GetPackageAsync(sourceId, packageId, token), cancellationToken);

    public Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
        queries.GetPackageForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);

    public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync($"packages:versions:{sourceId:N}:{packageId}", token => queries.ListVersionsAsync(sourceId, packageId, token), cancellationToken);

    public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
        queries.ListVersionsForWorkspaceAsync(workspaceId, sourceId, packageId, cancellationToken);

    public Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync($"packages:version:{sourceId:N}:{packageId}:{version}", token => queries.GetVersionAsync(sourceId, packageId, version, token), cancellationToken);

    public Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
        queries.GetVersionForWorkspaceAsync(workspaceId, sourceId, packageId, version, cancellationToken);

    public Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync("features:list", queries.ListFeaturesAsync, cancellationToken);

    public Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync($"features:item:{featureId}", token => queries.GetFeatureAsync(featureId, token), cancellationToken);

    private static IReadOnlyList<Guid> NormalizeSourceIds(IReadOnlyCollection<Guid>? sourceIds) =>
        sourceIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToList() ?? [];

    private static string CreateSourceCacheKey(IReadOnlyList<Guid> sourceIds) =>
        sourceIds.Count == 0 ? "all" : string.Join(",", sourceIds.Select(x => x.ToString("N")));
}

public sealed class PublicCatalogCache(IMemoryCache memoryCache) : IPublicCatalogCacheInvalidator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> keyLocks = new();
    private readonly object generationLock = new();
    private long generation;
    private CancellationTokenSource generationExpiration = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        var (generationKey, expirationToken) = CreateGenerationKey(key);
        if (memoryCache.TryGetValue(generationKey, out T? cachedValue))
            return cachedValue!;

        var keyLock = keyLocks.GetOrAdd(generationKey, _ => new SemaphoreSlim(1, 1));
        var acquired = false;
        try
        {
            await keyLock.WaitAsync(cancellationToken);
            acquired = true;

            if (memoryCache.TryGetValue(generationKey, out cachedValue))
                return cachedValue!;

            var value = await factory(cancellationToken);
            memoryCache.Set(generationKey, value, CreateCacheEntryOptions(expirationToken));
            return value;
        }
        finally
        {
            if (acquired)
                keyLock.Release();
        }
    }

    public void Invalidate()
    {
        CancellationTokenSource expiredGeneration;
        lock (generationLock)
        {
            generation++;
            expiredGeneration = generationExpiration;
            generationExpiration = new CancellationTokenSource();
        }

        expiredGeneration.Cancel();
        keyLocks.Clear();
    }

    private (string Key, CancellationToken ExpirationToken) CreateGenerationKey(string key)
    {
        lock (generationLock)
        {
            return ($"{generation}:{key}", generationExpiration.Token);
        }
    }

    private static MemoryCacheEntryOptions CreateCacheEntryOptions(CancellationToken expirationToken) =>
        new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
            SlidingExpiration = TimeSpan.FromMinutes(5)
        }.AddExpirationToken(new CancellationChangeToken(expirationToken));
}

public interface IPublicCatalogCacheInvalidator
{
    void Invalidate();
}

public interface IPublicCatalogQueries
{
    Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default);
    Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default);
    Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default);
    Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default);
    Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default);
    Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default);
}

public sealed record PublicPackageProjection(
    string PackageId,
    string DisplayName,
    PublicPackageSourceProjection Source,
    string? LatestVersion,
    IReadOnlyList<PublicPackageVersionProjection> Versions);

public sealed record PublicPackageVersionProjection(
    string PackageId,
    string Version,
    PublicPackageSourceProjection Source,
    string? SchemaVersion,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<PublicFeatureProjection> Features);

public sealed record PublicPackageSourceProjection(
    Guid Id,
    string Name,
    string Url);

public sealed record PublicFeatureProjection(
    string FeatureId,
    string PackageId,
    string PackageVersion,
    PublicPackageSourceProjection Source,
    string TypeName,
    string DisplayName,
    string? Description,
    string? Category,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<PublicDependencyProjection> Dependencies,
    IReadOnlyList<PublicConflictProjection> Conflicts,
    IReadOnlyList<PublicInfrastructureRequirementProjection> Infrastructure,
    bool Advanced,
    bool Experimental,
    string ExtensionsJson,
    IReadOnlyList<PublicFeatureSettingProjection> Settings);

public sealed record PublicFeatureSettingProjection(
    string Name,
    string? ClrType,
    string JsonType,
    bool Required,
    string? DefaultValueJson,
    string DisplayName,
    string? Description,
    string? Category,
    string ValidationJson,
    bool Secret,
    bool RestartRequired,
    string? EnvironmentVariable,
    string UiJson,
    string ExtensionsJson);

public sealed record PublicDependencyProjection(
    string? PackageId,
    string? VersionRange,
    string? FeatureId,
    bool Optional,
    string? Reason);

public sealed record PublicConflictProjection(
    string? PackageId,
    string? VersionRange,
    string? FeatureId,
    string? Reason);

public sealed record PublicInfrastructureRequirementProjection(
    string Id,
    string Kind,
    bool Optional,
    string? Reason,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> ConfigurationKeys,
    string ExtensionsJson);
