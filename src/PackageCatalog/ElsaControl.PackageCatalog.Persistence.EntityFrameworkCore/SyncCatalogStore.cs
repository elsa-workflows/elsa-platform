using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class SyncCatalogStore(CatalogDbContext dbContext) : ISyncCatalogStore
{
    public Task<Package?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) =>
        dbContext.Packages
            .Include(x => x.Versions)
            .ThenInclude(x => x.Features)
            .ThenInclude(x => x.Settings)
            .SingleOrDefaultAsync(x => x.SourceId == sourceId && x.PackageId == packageId, cancellationToken);

    public Task<PackageVersion?> GetPackageVersionAsync(Guid packageId, string version, CancellationToken cancellationToken = default) =>
        dbContext.PackageVersions
            .Include(x => x.Features)
            .ThenInclude(x => x.Settings)
            .SingleOrDefaultAsync(x => x.PackageId == packageId && x.Version == version, cancellationToken);

    public async Task AddPackageAsync(Package package, CancellationToken cancellationToken = default) =>
        await dbContext.Packages.AddAsync(package, cancellationToken);

    public async Task AddValidationResultAsync(ManifestValidationResultRecord result, CancellationToken cancellationToken = default) =>
        await dbContext.ManifestValidationResults.AddAsync(result, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
