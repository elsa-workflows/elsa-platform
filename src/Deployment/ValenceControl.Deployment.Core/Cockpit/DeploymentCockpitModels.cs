using ValenceControl.Deployment.Core.Workspace;

namespace ValenceControl.Deployment.Core.Cockpit;

public enum DeploymentHealth
{
    Healthy,
    Degraded,
    Unreachable
}

public enum DriftStatus
{
    InSync,
    DriftDetected,
    Unknown
}

public enum DeploymentStatus
{
    Succeeded,
    Running,
    Blocked,
    RolledBack
}

public enum CredentialVerificationStatus
{
    Verified,
    Missing,
    Expired,
    Unverified,
    NotVerifiable
}

public enum CapabilityBoundary
{
    Workflow,
    EngineApi,
    Shell,
    Hosting
}

public enum ValidationSeverity
{
    Pass,
    Warning,
    Blocker
}

public enum DiffCategory
{
    Workflows,
    Features,
    ShellConfiguration,
    RuntimeConfiguration,
    SecretReferences,
    Observability,
    EngineBindings
}

public enum DiffImpact
{
    Added,
    Changed,
    Removed
}

public enum PromotionArtifactImpact
{
    Added,
    Changed,
    Removed,
    Unchanged
}

public enum AssistantPlanStatus
{
    Proposed,
    Approved,
    Rejected,
    Executed
}

public enum EnvironmentTier
{
    Dev,
    Test,
    Stage,
    Production
}

public enum CertificateStatus
{
    Trusted,
    Untrusted,
    Expiring
}

public enum ObservabilityBindingKind
{
    Logs,
    Traces,
    Metrics,
    Console
}

public enum ObservabilityBindingStatus
{
    Connected,
    Degraded,
    Unavailable
}

public enum DeploymentValidationOutcome
{
    Passed,
    Warnings,
    Blocked
}

public enum DriftAction
{
    Review,
    Redeploy,
    Import
}

public sealed record RuntimeControl(
    string Id,
    string Label,
    CapabilityBoundary Boundary,
    string CapabilityId,
    string Description);

public sealed record EngineCapability(
    string Id,
    string Label,
    CapabilityBoundary Boundary);

public sealed record EngineEndpointMetadata(
    string BaseUrl,
    string Region,
    string Version,
    CertificateStatus CertificateStatus);

public sealed record EngineCredentialReference(
    string Provider,
    string Reference,
    CredentialVerificationStatus VerificationStatus,
    DateTimeOffset? LastVerifiedAt);

public sealed record WorkflowEngineRegistration(
    string Id,
    string Name,
    string EnvironmentId,
    EngineEndpointMetadata Endpoint,
    EngineCredentialReference CredentialReference,
    DeploymentHealth Health,
    DateTimeOffset? LastHeartbeatAt,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider,
    DateTimeOffset? LastVerificationAt = null,
    string VerificationMessage = "",
    EngineCredentialAssignmentStatus CredentialAssignmentStatus = EngineCredentialAssignmentStatus.Assigned);

public sealed record DesiredStateRevision(
    string Id,
    int Revision,
    string Commit,
    string Label,
    DateTimeOffset AuthoredAt);

public sealed record EnvironmentSummary(
    string Id,
    string Name,
    EnvironmentTier Tier,
    DeploymentHealth Health,
    DesiredStateRevision DesiredRevision,
    int? DeployedRevision,
    DeploymentStatus DeploymentStatus,
    DriftStatus DriftStatus,
    IReadOnlyList<string> EngineIds,
    string TierName = "",
    string TierStatus = "",
    IReadOnlyList<string>? TierCapabilities = null);

public sealed record WorkflowApplication(
    string Id,
    string Name,
    string WorkspaceName,
    IReadOnlyList<EnvironmentSummary> Environments);

public sealed record DeploymentDiffItem(
    string Id,
    DiffCategory Category,
    string Name,
    string SourceValue,
    string TargetValue,
    DiffImpact Impact);

public sealed record DeploymentValidation(
    string Id,
    ValidationSeverity Severity,
    string Scope,
    string Message);

public sealed record PromotionArtifactDigest(
    string Algorithm,
    string Value);

public sealed record PromotionArtifactDescriptor(
    string? ArtifactRecordId,
    string? ArtifactId,
    string? ArtifactTypeId,
    PromotionArtifactDigest? ContentDigest,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, string> Configuration,
    IReadOnlyList<string> CompatibilityHints);

public sealed record PromotionArtifactComparison(
    string Name,
    PromotionArtifactDescriptor? Source,
    PromotionArtifactDescriptor? Target,
    PromotionArtifactImpact Impact,
    IReadOnlyList<DeploymentValidation> RuntimeCompatibility);

public sealed record PromotionComparison(
    string SourceEnvironmentId,
    string TargetEnvironmentId,
    string SourceRevisionId,
    int SourceRevision,
    int TargetRevision,
    IReadOnlyList<DeploymentDiffItem> Diff,
    IReadOnlyList<DeploymentValidation> Validations,
    int? RollbackRevision,
    string? RollbackRevisionId)
{
    public IReadOnlyList<PromotionArtifactComparison> Artifacts { get; init; } = [];
}

public sealed record ObservabilityBinding(
    string Id,
    ObservabilityBindingKind Kind,
    string Provider,
    ObservabilityBindingStatus Status,
    string Scope,
    int CorrelatedRevision,
    string Sample);

public sealed record DeploymentHistoryEvent(
    string Id,
    string Status,
    int Revision,
    string Actor,
    string EnvironmentId,
    string EngineId,
    DeploymentValidationOutcome ValidationOutcome,
    DateTimeOffset OccurredAt,
    int? RollbackSourceRevision,
    IReadOnlyList<DeploymentRunCommandSummary> Commands);

public sealed record DriftReportItem(
    string Id,
    string EnvironmentId,
    string EngineId,
    string Area,
    string Desired,
    string Observed,
    DriftAction Action);

public sealed record AssistantPlan(
    string Id,
    int Version,
    AssistantPlanStatus Status,
    string WorkspaceName,
    string TargetEnvironmentId,
    string TargetEngineId,
    string Summary,
    IReadOnlyList<string> ProposedActions,
    IReadOnlyList<string> ExecutedActions,
    IReadOnlyList<DeploymentValidation> Validations,
    string RollbackPath,
    bool AllOrNothing,
    DateTimeOffset CreatedAt);

public sealed record DeploymentCockpit(
    IReadOnlyList<WorkflowApplication> Applications,
    IReadOnlyList<WorkflowEngineRegistration> Engines,
    IReadOnlyList<PromotionComparison> Comparisons,
    IReadOnlyList<ObservabilityBinding> ObservabilityBindings,
    IReadOnlyList<DeploymentHistoryEvent> History,
    IReadOnlyList<DriftReportItem> DriftReport,
    IReadOnlyList<AssistantPlan> AssistantPlans);
