using System.Data;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class AccountWorkspaceStore(CatalogDbContext dbContext) : IAccountWorkspaceStore
{
    public async Task<ExternalIdentityLookup?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken cancellationToken = default)
    {
        var identity = await dbContext.ExternalIdentities
            .AsNoTracking()
            .Include(x => x.Account)
            .ThenInclude(x => x!.Memberships)
            .ThenInclude(x => x.Workspace)
            .SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);

        if (identity?.Account is null)
            return null;

        return new ExternalIdentityLookup(
            identity.Id,
            new AccountWorkspaceContext(
                new AccountSummary(identity.Account.Id, identity.Account.DisplayName, identity.Account.Email),
                identity.Account.Memberships
                    .Where(x => x.Workspace is { SoftDeletedAt: null })
                    .Select(x => new WorkspaceSummary(x.Workspace!.Id, x.Workspace.Name, x.Workspace.Kind, x.Role))
                    .ToList()));
    }

    public async Task AddAccountAsync(Account account, CancellationToken cancellationToken = default) =>
        await dbContext.Accounts.AddAsync(account, cancellationToken);

    public async Task UpdateExternalIdentitySeenAsync(Guid externalIdentityId, string? displayName, string? email, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ExternalIdentities
            .Where(x => x.Id == externalIdentityId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.DisplayName, displayName)
                .SetProperty(x => x.Email, email)
                .SetProperty(x => x.LastSeenAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        await dbContext.Accounts
            .Where(x => x.ExternalIdentities.Any(identity => identity.Id == externalIdentityId))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.DisplayName, displayName)
                .SetProperty(x => x.Email, email)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WorkspaceEntitlementSnapshot?> GetLatestEntitlementAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkspaceEntitlementSnapshots
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.SyncedAt)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> WorkspaceExistsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(x => x.Id == workspaceId && x.SoftDeletedAt == null, cancellationToken);

    public async Task<WorkspaceEntitlementSnapshot> SaveEntitlementAsync(WorkspaceEntitlementSnapshot entitlement, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.WorkspaceEntitlementSnapshots
            .SingleOrDefaultAsync(x => x.WorkspaceId == entitlement.WorkspaceId, cancellationToken);

        if (existing is null)
        {
            entitlement.SyncedAt = now;
            entitlement.CreatedAt = now;
            entitlement.UpdatedAt = now;
            await dbContext.WorkspaceEntitlementSnapshots.AddAsync(entitlement, cancellationToken);
            existing = entitlement;
        }
        else
        {
            existing.CanCreateCustomSources = entitlement.CanCreateCustomSources;
            existing.MaxSources = entitlement.MaxSources;
            existing.MaxPackagesIndexed = entitlement.MaxPackagesIndexed;
            existing.MaxVersionsPerPackage = entitlement.MaxVersionsPerPackage;
            existing.MaxSyncsPerDay = entitlement.MaxSyncsPerDay;
            existing.PrivateFeedsEnabled = entitlement.PrivateFeedsEnabled;
            existing.SyncedAt = now;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<WorkspaceSourceAddResult> TryAddWorkspaceSourceAsync(PackageSource source, int maxSources, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var currentSourceCount = await dbContext.PackageSources.CountAsync(x =>
            x.OwnerWorkspaceId == source.OwnerWorkspaceId &&
            x.Visibility == PackageSourceVisibility.Workspace &&
            x.SoftDeletedAt == null,
            cancellationToken);
        if (currentSourceCount >= maxSources)
            return new WorkspaceSourceAddResult(WorkspaceSourceAddStatus.LimitReached);

        var urlExists = await dbContext.PackageSources.AnyAsync(x =>
            x.OwnerWorkspaceId == source.OwnerWorkspaceId &&
            x.Visibility == PackageSourceVisibility.Workspace &&
            x.SoftDeletedAt == null &&
            x.Url == source.Url,
            cancellationToken);
        if (urlExists)
            return new WorkspaceSourceAddResult(WorkspaceSourceAddStatus.DuplicateUrl);

        await dbContext.PackageSources.AddAsync(source, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WorkspaceSourceAddResult(WorkspaceSourceAddStatus.Created);
    }

    public async Task<IReadOnlyList<PackageSource>> ListVisibleSourcesAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        await dbContext.PackageSources
            .AsNoTracking()
            .Where(x => x.Enabled && x.Browseable && x.SoftDeletedAt == null)
            .Where(x =>
                (x.Visibility == PackageSourceVisibility.Public && x.OwnerWorkspaceId == null) ||
                (x.Visibility == PackageSourceVisibility.Workspace && x.OwnerWorkspaceId == workspaceId && x.OwnerWorkspace!.SoftDeletedAt == null))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default) =>
        await dbContext.Packages
            .AsNoTracking()
            .Where(x => sourceIds.Contains(x.SourceId))
            .GroupBy(x => x.SourceId)
            .Select(x => new { SourceId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.SourceId, x => x.Count, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            dbContext.ChangeTracker.Clear();
            throw new AccountWorkspaceConflictException("A concurrent account workspace update conflicted with this operation.", ex);
        }
    }
}
