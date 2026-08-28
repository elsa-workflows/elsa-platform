namespace ElsaControl.PackageCatalog.Abstractions.Deployment;

public interface IDeploymentPackageCatalog
{
    Task<DeploymentPackageValidationResult> ValidateRequirementsAsync(DeploymentPackageValidationRequest request, CancellationToken cancellationToken = default);
}

public sealed record DeploymentPackageValidationRequest(
    string? ElsaVersion,
    string? RuntimeImageVersion,
    IReadOnlyList<DeploymentPackageRequirement> Requirements,
    Guid? WorkspaceId = null);

public sealed record DeploymentPackageRequirement(
    string PackageId,
    string? Version = null,
    string? VersionRange = null,
    Guid? SourceId = null,
    IReadOnlyList<string>? Features = null,
    bool Required = true);

public sealed record DeploymentPackageValidationResult(
    bool Succeeded,
    IReadOnlyList<DeploymentPackageResolution> Resolutions,
    IReadOnlyList<DeploymentPackageFinding> Findings)
{
    public bool HasErrors => Findings.Any(x => x.Severity == DeploymentPackageFindingSeverity.Error);
}

public sealed record DeploymentPackageResolution(
    DeploymentPackageRequirement Requirement,
    Guid? SourceId,
    string? ResolvedVersion,
    PackageManifestValidationState Manifest,
    PackageApprovalState Approval,
    PackageTrustState Trust,
    PackageSuspicionState Suspicion,
    PackageCompatibilityState Compatibility);

public sealed record DeploymentPackageFinding(
    DeploymentPackageFindingSeverity Severity,
    DeploymentPackageFindingCategory Category,
    string Code,
    string Message,
    string? PackageId = null,
    string? Version = null,
    string? FeatureId = null);

public enum DeploymentPackageFindingSeverity
{
    Info,
    Warning,
    Error
}

public enum DeploymentPackageFindingCategory
{
    Discovery,
    ManifestValidation,
    Approval,
    Trust,
    Suspicion,
    Compatibility,
    Feature,
    Conflict
}

public enum PackageManifestValidationState
{
    Unknown,
    Missing,
    Valid,
    Invalid,
    UnsupportedSchema
}

public enum PackageApprovalState
{
    Unknown,
    Pending,
    Approved,
    Rejected
}

public enum PackageTrustState
{
    Unknown,
    Trusted,
    Untrusted
}

public enum PackageSuspicionState
{
    Unknown,
    Clean,
    Suspicious
}

public enum PackageCompatibilityState
{
    Unknown,
    Compatible,
    Incompatible
}
