using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sync;

namespace Elsa.Platform.PackageCatalog.Core.Persistence;

public interface ICatalogStore
{
    IQueryable<PackageSource> PackageSources { get; }
    IQueryable<Package> Packages { get; }
    IQueryable<PackageVersion> PackageVersions { get; }
    IQueryable<SyncRun> SyncRuns { get; }
    IQueryable<SyncRunItem> SyncRunItems { get; }
    IQueryable<ApprovalRecord> ApprovalRecords { get; }
    Task AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
