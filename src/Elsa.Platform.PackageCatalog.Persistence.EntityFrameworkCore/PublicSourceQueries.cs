using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sources;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class PublicSourceQueries(CatalogDbContext dbContext) : IPublicSourceQueries
{
    public async Task<IReadOnlyList<PublicSourceProjection>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        var sources = await dbContext.PackageSources
            .AsNoTracking()
            .Where(x => x.Enabled && x.Browseable && x.SoftDeletedAt == null && x.Visibility == PackageSourceVisibility.Public && x.OwnerWorkspaceId == null)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Url,
                PackageCount = x.Packages.Count(package =>
                    package.Approved &&
                    package.Listed &&
                    package.Versions.Any(version =>
                        version.IsListed &&
                        version.ApprovalStatus == PackageApprovalStatus.Approved &&
                        version.ValidationStatus == ValidationStatus.Valid &&
                        !version.SuspiciousChangeDetected))
            })
            .ToListAsync(cancellationToken);

        return sources
            .Select(x => new PublicSourceProjection(x.Id, x.Name, PublicSourceUrlSanitizer.Sanitize(x.Url), x.PackageCount))
            .ToList();
    }
}
