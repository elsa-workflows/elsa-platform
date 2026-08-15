using ValenceControl.PackageCatalog.Abstractions.Compatibility;
using ValenceControl.PackageCatalog.Core.Compatibility;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.PackageCatalog.Core.Packages;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;

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

    public async Task<IReadOnlyList<PackageVersion>> GetPackageVersionsAsync(
        Guid? workspaceId,
        IReadOnlyList<SelectedPackageVersion> packages,
        CancellationToken cancellationToken = default)
    {
        if (packages.Count == 0)
            return [];

        var sourceIds = packages.Select(package => package.SourceId).Distinct().ToList();
        var packageIds = packages.Select(package => package.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var versions = packages.Select(package => package.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var requested = packages
            .Select(CreateSelectionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = await dbContext.PackageVersions
            .AsNoTracking()
            .Include(x => x.Package)
            .Where(x => x.Package != null
                && x.Package.Source != null
                && x.Package.Source.Enabled
                && x.Package.Source.Browseable
                && x.Package.Source.SoftDeletedAt == null
                && ((x.Package.Source.Visibility == PackageSourceVisibility.Public && x.Package.Source.OwnerWorkspaceId == null) ||
                    (workspaceId.HasValue && x.Package.Source.Visibility == PackageSourceVisibility.Workspace && x.Package.Source.OwnerWorkspaceId == workspaceId.Value))
                && sourceIds.Contains(x.Package.SourceId)
                && packageIds.Contains(x.Package.PackageId)
                && versions.Contains(x.Version))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(candidate => candidate.Package is not null && requested.Contains(CreateSelectionKey(
                new SelectedPackageVersion(candidate.Package.SourceId, candidate.Package.PackageId, candidate.Version))))
            .ToList();
    }

    private static string CreateSelectionKey(SelectedPackageVersion package) =>
        $"{package.SourceId:N}\0{package.PackageId}\0{package.Version}";
}
