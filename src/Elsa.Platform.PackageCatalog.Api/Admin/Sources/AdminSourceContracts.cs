using Elsa.Platform.PackageCatalog.Core.Packages;

namespace Elsa.Platform.PackageCatalog.Api.Admin.Sources;

public sealed record AdminSourceRequest(
    string Name,
    string Url,
    bool Enabled,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string>? ExcludePatterns,
    PackageSourceApprovalPolicy ApprovalPolicy,
    PackageSourceVersionDiscoveryPolicy VersionDiscoveryPolicy = PackageSourceVersionDiscoveryPolicy.AllVersions,
    string? PollingInterval = null);

public sealed record AdminSourceResponse(
    Guid Id,
    string Name,
    PackageSourceType Type,
    string Url,
    bool Enabled,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns,
    PackageSourceApprovalPolicy ApprovalPolicy,
    PackageSourceVersionDiscoveryPolicy VersionDiscoveryPolicy,
    PackageSourceStatus Status,
    bool IsSyncing,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset? LastSuccessfulSyncAt,
    string? LastSyncError,
    int PackageCount,
    DateTimeOffset? SoftDeletedAt,
    string? PollingInterval,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminValidationErrorResponse(IReadOnlyList<string> Errors);
