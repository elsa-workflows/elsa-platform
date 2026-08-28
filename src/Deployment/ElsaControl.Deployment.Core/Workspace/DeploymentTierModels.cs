namespace ElsaControl.Deployment.Core.Workspace;

public enum DeploymentTierStatus
{
    Active,
    Archived
}

public enum DeploymentTierCapabilityCategory
{
    Classification,
    Promotion,
    Safeguards,
    Validation,
    Rollback,
    Observability
}

public static class DeploymentTierCapabilities
{
    public const string DevelopmentLike = "deployment.tier.development-like";
    public const string TestLike = "deployment.tier.test-like";
    public const string PreproductionLike = "deployment.tier.preproduction-like";
    public const string ProductionLike = "deployment.tier.production-like";
    public const string PromotionSource = "deployment.promotion.source";
    public const string PromotionTarget = "deployment.promotion.target";
    public const string ConfirmationRequired = "deployment.confirmation.required";
    public const string RollbackEnabled = "deployment.rollback.enabled";
    public const string SecretVerificationRequired = "deployment.secret-verification.required";
    public const string ObservabilityRequired = "deployment.observability.required";
}

public sealed record DeploymentTierCapability(
    string Id,
    string Label,
    string Description,
    DeploymentTierCapabilityCategory Category,
    bool IsDeprecated = false);

public sealed record DeploymentTierProfile(
    Guid Id,
    string Name,
    DeploymentTierStatus Status,
    IReadOnlyList<string> Capabilities,
    bool RequiresReview = false);

public sealed record WorkspaceDeploymentTier(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    int SortOrder,
    bool IsDefault,
    DeploymentTierStatus Status,
    IReadOnlyList<string> Capabilities,
    int EnvironmentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CreatedByAccountId,
    Guid? UpdatedByAccountId,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByAccountId);

public sealed record DeploymentTierEnvironmentSample(
    Guid ApplicationId,
    string ApplicationName,
    Guid EnvironmentId,
    string EnvironmentName);

public sealed record DeploymentTierImpactSummary(
    Guid TierId,
    IReadOnlyList<string> CurrentCapabilities,
    IReadOnlyList<string> ProposedCapabilities,
    IReadOnlyList<string> AddedCapabilities,
    IReadOnlyList<string> RemovedCapabilities,
    int AffectedEnvironmentCount,
    IReadOnlyList<DeploymentTierEnvironmentSample> AffectedEnvironmentSamples,
    IReadOnlyList<string> ChangedSafeguards);

public sealed record CreateDeploymentTierRequest(
    string Name,
    string? Description,
    int SortOrder,
    IReadOnlyList<string> Capabilities,
    Guid? ActorAccountId);

public sealed record UpdateDeploymentTierRequest(
    string Name,
    string? Description,
    int SortOrder,
    IReadOnlyList<string> Capabilities,
    bool ImpactAccepted,
    Guid? ActorAccountId);

public sealed record PreviewDeploymentTierImpactRequest(
    IReadOnlyList<string> Capabilities);

public sealed record ArchiveDeploymentTierRequest(Guid? ActorAccountId);

public sealed record RestoreDeploymentTierRequest(Guid? ActorAccountId);

public sealed record DeploymentTierChangeRecord(
    Guid Id,
    Guid WorkspaceId,
    Guid TierId,
    Guid? ActorAccountId,
    string ChangeType,
    string Summary,
    DateTimeOffset ChangedAt,
    int AffectedEnvironmentCount);
