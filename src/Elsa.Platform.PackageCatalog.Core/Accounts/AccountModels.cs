namespace Elsa.Platform.PackageCatalog.Core.Accounts;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ExternalIdentity> ExternalIdentities { get; set; } = [];
    public List<WorkspaceMembership> Memberships { get; set; } = [];
}

public sealed class ExternalIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public WorkspaceKind Kind { get; set; } = WorkspaceKind.Personal;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SoftDeletedAt { get; set; }
    public List<WorkspaceMembership> Memberships { get; set; } = [];
    public List<WorkspaceEntitlementSnapshot> EntitlementSnapshots { get; set; } = [];
}

public sealed class WorkspaceMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Reader;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkspaceEntitlementSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public bool CanCreateCustomSources { get; set; }
    public int MaxSources { get; set; }
    public int? MaxPackagesIndexed { get; set; }
    public int? MaxVersionsPerPackage { get; set; }
    public int? MaxSyncsPerDay { get; set; }
    public bool PrivateFeedsEnabled { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum WorkspaceKind
{
    Personal,
    Organization
}

public enum WorkspaceRole
{
    Reader,
    SourceAdmin,
    Owner
}

public enum PackageSourceVisibility
{
    Public,
    Workspace
}
