using Elsa.Catalog.Core.Packages;

namespace Elsa.Catalog.Core.Sources;

public sealed class PackageSourceService(IPackageSourceStore store, PackageSourceValidator validator, IPublicCatalogCacheInvalidator? publicCatalogCache = null)
{
    public Task<IReadOnlyList<PackageSource>> ListAsync(CancellationToken cancellationToken = default) =>
        store.ListAsync(cancellationToken);

    public Task<PackageSource?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default) =>
        store.GetPackageCountsAsync(sourceIds, cancellationToken);

    public async Task<int> GetPackageCountAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var counts = await store.GetPackageCountsAsync([sourceId], cancellationToken);
        return counts.GetValueOrDefault(sourceId);
    }

    public async Task<PackageSourceResult> CreateAsync(PackageSource source, CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(source);
        if (!validation.IsValid)
            return PackageSourceResult.Invalid(validation.Errors);

        source.CreatedAt = DateTimeOffset.UtcNow;
        source.UpdatedAt = source.CreatedAt;
        await store.AddAsync(source, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        publicCatalogCache?.Invalidate();
        return PackageSourceResult.Success(source);
    }

    public async Task<PackageSourceResult> UpdateAsync(Guid id, PackageSource source, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetAsync(id, cancellationToken);
        if (existing is null)
            return PackageSourceResult.NotFound;

        var validation = validator.Validate(source);
        if (!validation.IsValid)
            return PackageSourceResult.Invalid(validation.Errors);

        existing.Name = source.Name;
        existing.Type = source.Type;
        existing.Url = source.Url;
        existing.Enabled = source.Enabled;
        existing.IncludePatterns = source.IncludePatterns;
        existing.ExcludePatterns = source.ExcludePatterns;
        existing.ApprovalPolicy = source.ApprovalPolicy;
        existing.VersionDiscoveryPolicy = source.VersionDiscoveryPolicy;
        existing.PollingInterval = source.PollingInterval;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await store.SaveChangesAsync(cancellationToken);
        publicCatalogCache?.Invalidate();
        return PackageSourceResult.Success(existing);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await store.GetAsync(id, cancellationToken);
        if (source is null)
            return false;

        source.Enabled = false;
        source.SoftDeletedAt = DateTimeOffset.UtcNow;
        source.UpdatedAt = source.SoftDeletedAt.Value;
        await store.SaveChangesAsync(cancellationToken);
        publicCatalogCache?.Invalidate();
        return true;
    }
}

public interface IPackageSourceStore
{
    Task<IReadOnlyList<PackageSource>> ListAsync(CancellationToken cancellationToken = default);
    Task<PackageSource?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default);
    Task AddAsync(PackageSource source, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record PackageSourceResult(PackageSource? Source, IReadOnlyList<string> Errors, bool NotFoundResult)
{
    public bool Succeeded => Source is not null && Errors.Count == 0 && !NotFoundResult;
    public static PackageSourceResult Success(PackageSource source) => new(source, [], false);
    public static PackageSourceResult Invalid(IReadOnlyList<string> errors) => new(null, errors, false);
    public static PackageSourceResult NotFound { get; } = new(null, [], true);
}
