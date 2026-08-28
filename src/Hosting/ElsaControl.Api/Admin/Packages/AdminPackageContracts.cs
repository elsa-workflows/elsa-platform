using ElsaControl.PackageCatalog.Core.Packages;

namespace ElsaControl.Api.Admin.Packages;

public sealed record AdminPackageResponse(
    string PackageId,
    bool Approved,
    bool Listed,
    Guid? SourceId,
    AdminPackageSourceResponse? Source,
    string? LatestVersion,
    PackageApprovalStatus ApprovalStatus,
    ValidationStatus ValidationStatus,
    int FeaturesCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdminPackageVersionResponse> Versions);

public sealed record AdminPackageSourceResponse(
    Guid Id,
    string Name,
    string Url,
    bool Enabled,
    PackageSourceStatus Status,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset? LastSuccessfulSyncAt);

public sealed record AdminPackageVersionResponse(
    string Version,
    ValidationStatus ValidationStatus,
    PackageApprovalStatus ApprovalStatus,
    bool IsListed,
    bool SuspiciousChangeDetected,
    string? SchemaVersion,
    string ManifestHash,
    string? SuspiciousManifestHash,
    string VersionStateToken,
    DateTimeOffset? PublishedAt,
    DateTimeOffset IndexedAt,
    int FeaturesCount,
    int SettingsCount,
    AdminCompatibilityResponse Compatibility,
    IReadOnlyList<AdminVisibilityReasonResponse> VisibilityReasons,
    IReadOnlyList<AdminFeatureResponse> Features,
    AdminManifestResponse Manifest);

public sealed record AdminPackageListResponse(
    string PackageId,
    bool Approved,
    bool Listed,
    Guid? SourceId,
    string? LatestVersion,
    PackageApprovalStatus ApprovalStatus,
    ValidationStatus ValidationStatus,
    int FeaturesCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdminPackageListVersionResponse> Versions);

public sealed record AdminPackageListVersionResponse(
    string Version,
    ValidationStatus ValidationStatus,
    PackageApprovalStatus ApprovalStatus,
    bool IsListed,
    bool SuspiciousChangeDetected,
    string? SchemaVersion,
    string VersionStateToken);

public sealed record AdminCompatibilityResponse(
    IReadOnlyList<string> TargetFrameworks,
    string? ElsaVersionRange,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> UnsupportedCombinations);

public sealed record AdminVisibilityReasonResponse(
    string Code,
    string Category,
    string Severity,
    string Message,
    bool BlocksPublicVisibility);

public sealed record AdminFeatureResponse(
    string FeatureId,
    string TypeName,
    string DisplayName,
    string? Description,
    string? Category,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> RequiredCapabilities,
    string DependenciesJson,
    string ConflictsJson,
    string InfrastructureJson,
    bool Advanced,
    bool Experimental,
    string ExtensionsJson,
    IReadOnlyList<AdminFeatureSettingResponse> Settings);

public sealed record AdminFeatureSettingResponse(
    string Name,
    string? ClrType,
    string JsonType,
    bool Required,
    string? DefaultValueJson,
    string DisplayName,
    string? Description,
    string? Category,
    string ValidationJson,
    bool Secret,
    bool RestartRequired,
    string? EnvironmentVariable,
    string UiJson,
    string ExtensionsJson);

public sealed record AdminManifestResponse(
    bool Available,
    string? SchemaVersion,
    string ManifestHash,
    string? SuspiciousManifestHash,
    string ManifestJson);

public sealed record AdminVersionManifestResponse(
    string PackageId,
    string Version,
    bool Available,
    string? SchemaVersion,
    string ManifestHash,
    string? SuspiciousManifestHash,
    string ManifestJson);

public sealed record ApprovalRequest(string? Reason, string? ExpectedStateToken);

public sealed record AdminValidationResultResponse(
    Guid Id,
    string? SchemaVersion,
    ValidationStatus Status,
    string ErrorsJson,
    string WarningsJson,
    DateTimeOffset ValidatedAt,
    string? ValidatorVersion);

public sealed record AdminValidationFindingsResponse(
    string PackageId,
    string Version,
    IReadOnlyList<AdminValidationFindingResponse> Findings);

public sealed record AdminValidationFindingResponse(
    string Severity,
    string? Code,
    string Message,
    string? Path,
    bool BlocksPublicVisibility,
    DateTimeOffset ValidatedAt,
    string? ValidatorVersion);
