using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;

namespace Elsa.Platform.Api.Workspace;

public sealed record AccountContextResponse(Guid Id, string? DisplayName, string? Email);

public sealed record WorkspaceContextResponse(Guid Id, string Name, WorkspaceKind Kind, WorkspaceRole Role);

public sealed record MeWorkspacesResponse(AccountContextResponse Account, IReadOnlyList<WorkspaceContextResponse> Workspaces);

public sealed record WorkspaceSourceResponse(
    Guid Id,
    string Name,
    string Url,
    PackageSourceVisibility Ownership,
    int PackageCount);

public sealed record WorkspaceSourceRequest(
    string Name,
    string Url,
    bool Enabled,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string>? ExcludePatterns,
    PackageSourceVersionDiscoveryPolicy VersionDiscoveryPolicy = PackageSourceVersionDiscoveryPolicy.AllVersions);

public sealed record WorkspaceValidationErrorResponse(IReadOnlyList<string> Errors);

public sealed record WorkspaceEntitlementRequest(
    bool CanCreateCustomSources,
    int MaxSources,
    int? MaxPackagesIndexed,
    int? MaxVersionsPerPackage,
    int? MaxSyncsPerDay,
    bool PrivateFeedsEnabled);

public sealed record WorkspaceEntitlementResponse(
    Guid WorkspaceId,
    bool CanCreateCustomSources,
    int MaxSources,
    int? MaxPackagesIndexed,
    int? MaxVersionsPerPackage,
    int? MaxSyncsPerDay,
    bool PrivateFeedsEnabled,
    DateTimeOffset SyncedAt);
