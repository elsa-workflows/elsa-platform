using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Persistence;
using Elsa.Platform.PackageCatalog.Core.Sync;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class EfCoreCatalogStore(CatalogDbContext dbContext) : ICatalogStore
{
    public IQueryable<PackageSource> PackageSources => dbContext.PackageSources;
    public IQueryable<Package> Packages => dbContext.Packages;
    public IQueryable<PackageVersion> PackageVersions => dbContext.PackageVersions;
    public IQueryable<SyncRun> SyncRuns => dbContext.SyncRuns;
    public IQueryable<SyncRunItem> SyncRunItems => dbContext.SyncRunItems;
    public IQueryable<ApprovalRecord> ApprovalRecords => dbContext.ApprovalRecords;

    public async Task AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class =>
        await dbContext.Set<T>().AddAsync(entity, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
