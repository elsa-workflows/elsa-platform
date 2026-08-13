using ValenceControl.PackageCatalog.Core.Accounts;

namespace ValenceControl.PackageCatalog.Core.Packages;

public sealed class PackageSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public PackageSourceType Type { get; set; } = PackageSourceType.NuGetFeed;
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Browseable { get; set; } = true;
    public PackageSourceVisibility Visibility { get; set; } = PackageSourceVisibility.Public;
    public Guid? OwnerWorkspaceId { get; set; }
    public Workspace? OwnerWorkspace { get; set; }
    public List<string> IncludePatterns { get; set; } = [];
    public List<string> ExcludePatterns { get; set; } = [];
    public PackageSourceApprovalPolicy ApprovalPolicy { get; set; } = PackageSourceApprovalPolicy.Manual;
    public PackageSourceVersionDiscoveryPolicy VersionDiscoveryPolicy { get; set; } = PackageSourceVersionDiscoveryPolicy.AllVersions;
    public PackageSourceStatus Status { get; set; } = PackageSourceStatus.Healthy;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
    public string? LastSyncError { get; set; }
    public string? PollingInterval { get; set; }
    public DateTimeOffset? SoftDeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Package> Packages { get; set; } = [];
}

public sealed class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PackageId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Guid SourceId { get; set; }
    public PackageSource? Source { get; set; }
    public bool Approved { get; set; }
    public bool Listed { get; set; } = true;
    public string? LatestVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PackageVersion> Versions { get; set; } = [];
}

public sealed class PackageVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }
    public string Version { get; set; } = "";
    public string ManifestJson { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string? NuspecJson { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
    public ValidationStatus ValidationStatus { get; set; } = ValidationStatus.NotValidated;
    public string? ValidationErrors { get; set; }
    public PackageApprovalStatus ApprovalStatus { get; set; } = PackageApprovalStatus.Pending;
    public bool IsListed { get; set; } = true;
    public bool SuspiciousChangeDetected { get; set; }
    public string? SuspiciousManifestHash { get; set; }
    public string? SchemaVersion { get; set; }
    public List<Manifests.FeatureRecord> Features { get; set; } = [];
}

public sealed record PackageVersionContentChange(
    bool IsSuspicious,
    string? ObservedHash);
