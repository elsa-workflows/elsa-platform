namespace Elsa.Platform.PackageCatalog.Core.Accounts;

public sealed class AccountWorkspaceService(IAccountWorkspaceStore store)
{
    public async Task<AccountWorkspaceContext> GetOrCreateAsync(TrustedWorkspaceIdentity identity, CancellationToken cancellationToken = default)
    {
        var normalized = identity.Normalize();
        var existing = await store.FindByExternalIdentityAsync(normalized.Issuer, normalized.Subject, cancellationToken);
        if (existing is not null)
        {
            await store.UpdateExternalIdentitySeenAsync(existing.ExternalIdentityId, normalized.DisplayName, normalized.Email, cancellationToken);
            return existing.Context with
            {
                Account = existing.Context.Account with
                {
                    DisplayName = normalized.DisplayName,
                    Email = normalized.Email
                }
            };
        }

        var account = new Account
        {
            DisplayName = normalized.DisplayName,
            Email = normalized.Email
        };
        var externalIdentity = new ExternalIdentity
        {
            Account = account,
            Issuer = normalized.Issuer,
            Subject = normalized.Subject,
            DisplayName = normalized.DisplayName,
            Email = normalized.Email
        };
        var workspace = new Workspace
        {
            Name = string.IsNullOrWhiteSpace(normalized.DisplayName) ? "Personal Workspace" : normalized.DisplayName,
            Kind = WorkspaceKind.Personal
        };
        var membership = new WorkspaceMembership
        {
            Account = account,
            Workspace = workspace,
            Role = WorkspaceRole.Owner
        };

        account.ExternalIdentities.Add(externalIdentity);
        account.Memberships.Add(membership);
        workspace.Memberships.Add(membership);

        await store.AddAccountAsync(account, cancellationToken);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (AccountWorkspaceConflictException)
        {
            var concurrent = await store.FindByExternalIdentityAsync(normalized.Issuer, normalized.Subject, cancellationToken);
            if (concurrent is null)
                throw;

            await store.UpdateExternalIdentitySeenAsync(concurrent.ExternalIdentityId, normalized.DisplayName, normalized.Email, cancellationToken);
            return concurrent.Context with
            {
                Account = concurrent.Context.Account with
                {
                    DisplayName = normalized.DisplayName,
                    Email = normalized.Email
                }
            };
        }

        return new AccountWorkspaceContext(
            new AccountSummary(account.Id, account.DisplayName, account.Email),
            [new WorkspaceSummary(workspace.Id, workspace.Name, workspace.Kind, WorkspaceRole.Owner)]);
    }

    public async Task<WorkspaceAccess?> GetWorkspaceAccessAsync(TrustedWorkspaceIdentity identity, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateAsync(identity, cancellationToken);
        var workspace = context.Workspaces.SingleOrDefault(x => x.Id == workspaceId);
        return workspace is null ? null : new WorkspaceAccess(context.Account.Id, workspace.Id, workspace.Role);
    }
}

public interface IAccountWorkspaceStore
{
    Task<ExternalIdentityLookup?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken cancellationToken = default);
    Task AddAccountAsync(Account account, CancellationToken cancellationToken = default);
    Task UpdateExternalIdentitySeenAsync(Guid externalIdentityId, string? displayName, string? email, CancellationToken cancellationToken = default);
    Task<bool> WorkspaceExistsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceEntitlementSnapshot?> GetLatestEntitlementAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceEntitlementSnapshot> SaveEntitlementAsync(WorkspaceEntitlementSnapshot entitlement, CancellationToken cancellationToken = default);
    Task<WorkspaceSourceAddResult> TryAddWorkspaceSourceAsync(Packages.PackageSource source, int maxSources, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Packages.PackageSource>> ListVisibleSourcesAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> GetPackageCountsAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record TrustedWorkspaceIdentity(string Issuer, string Subject, string? DisplayName, string? Email)
{
    public TrustedWorkspaceIdentity Normalize() =>
        new(Issuer.Trim(), Subject.Trim(), NormalizeBlank(DisplayName), NormalizeBlank(Email));

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ExternalIdentityLookup(Guid ExternalIdentityId, AccountWorkspaceContext Context);

public sealed class AccountWorkspaceConflictException(string message, Exception innerException) : Exception(message, innerException);

public sealed record AccountWorkspaceContext(AccountSummary Account, IReadOnlyList<WorkspaceSummary> Workspaces);

public sealed record AccountSummary(Guid Id, string? DisplayName, string? Email);

public sealed record WorkspaceSummary(Guid Id, string Name, WorkspaceKind Kind, WorkspaceRole Role);

public sealed record WorkspaceAccess(Guid AccountId, Guid WorkspaceId, WorkspaceRole Role)
{
    public bool CanAdministerSources => Role is WorkspaceRole.Owner or WorkspaceRole.SourceAdmin;
}

public sealed record WorkspaceSourceAddResult(WorkspaceSourceAddStatus Status);

public enum WorkspaceSourceAddStatus
{
    Created,
    LimitReached,
    DuplicateUrl
}
