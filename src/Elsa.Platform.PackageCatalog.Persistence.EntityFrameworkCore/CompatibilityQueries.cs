using Elsa.Platform.PackageCatalog.Core.Compatibility;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class CompatibilityQueries(CatalogDbContext dbContext) : ICompatibilityQueries
{
    public Task<PackageVersion?> GetPackageVersionAsync(Guid? workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) =>
        dbContext.PackageVersions
            .AsNoTracking()
            .Include(x => x.Package)
            .SingleOrDefaultAsync(
                x => x.Package != null
                    && x.Package.Source != null
                    && x.Package.Source.Enabled
                    && x.Package.Source.Browseable
                    && x.Package.Source.SoftDeletedAt == null
                    && ((x.Package.Source.Visibility == PackageSourceVisibility.Public && x.Package.Source.OwnerWorkspaceId == null) ||
                        (workspaceId.HasValue && x.Package.Source.Visibility == PackageSourceVisibility.Workspace && x.Package.Source.OwnerWorkspaceId == workspaceId.Value))
                    && x.Package.SourceId == sourceId
                    && x.Package.PackageId == packageId
                    && x.Version == version,
                cancellationToken);
}
