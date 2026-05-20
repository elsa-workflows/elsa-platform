using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sources;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class PackageSourceStore(CatalogDbContext dbContext) : IPackageSourceStore
{
    public async Task<IReadOnlyList<PackageSource>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.PackageSources
            .Where(x => x.SoftDeletedAt == null)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public Task<PackageSource?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PackageSources
            .SingleOrDefaultAsync(x => x.Id == id && x.SoftDeletedAt == null, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default) =>
        await dbContext.Packages
            .Where(x => sourceIds.Contains(x.SourceId))
            .GroupBy(x => x.SourceId)
            .Select(x => new { SourceId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.SourceId, x => x.Count, cancellationToken);

    public async Task AddAsync(PackageSource source, CancellationToken cancellationToken = default) =>
        await dbContext.PackageSources.AddAsync(source, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
