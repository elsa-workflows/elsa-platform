using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Models;

internal sealed class DeploymentApplicationEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public List<DeploymentEnvironmentEntity> Environments { get; set; } = [];
}

internal sealed class DeploymentEnvironmentEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public DeploymentApplicationEntity? Application { get; set; }
    public string Name { get; set; } = "";
    public EnvironmentTier Tier { get; set; }
    public Guid? TierId { get; set; }
    public DeploymentTierDefinitionEntity? TierDefinition { get; set; }
    public bool TierRequiresReview { get; set; }
    public Guid? DesiredRevisionId { get; set; }
    public Guid? DeployedRevisionId { get; set; }
    public DeploymentStatus DeploymentStatus { get; set; }
    public DriftStatus DriftStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<WorkflowEngineEntity> Engines { get; set; } = [];
    public List<DesiredStateRevisionEntity> Revisions { get; set; } = [];
    public List<ObservabilityBindingEntity> ObservabilityBindings { get; set; } = [];
    public List<DriftReportItemEntity> DriftReports { get; set; } = [];
}

internal sealed class DeploymentTierDefinitionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public DeploymentTierStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedByAccountId { get; set; }
    public List<DeploymentTierCapabilityAssignmentEntity> Capabilities { get; set; } = [];
    public List<DeploymentEnvironmentEntity> Environments { get; set; } = [];
    public List<DeploymentTierChangeRecordEntity> Changes { get; set; } = [];
}

internal sealed class DeploymentTierCapabilityAssignmentEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TierId { get; set; }
    public DeploymentTierDefinitionEntity? Tier { get; set; }
    public string CapabilityId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
}

internal sealed class DeploymentTierChangeRecordEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TierId { get; set; }
    public DeploymentTierDefinitionEntity? Tier { get; set; }
    public Guid? ActorAccountId { get; set; }
    public string ChangeType { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
    public int AffectedEnvironmentCount { get; set; }
}

internal sealed class WorkflowEngineEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public Guid? CredentialReferenceId { get; set; }
    public DeploymentCredentialReferenceEntity? CredentialReferenceMetadata { get; set; }
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? Region { get; set; }
    public string? Version { get; set; }
    public CertificateStatus CertificateStatus { get; set; }
    public string CredentialProvider { get; set; } = "";
    public string CredentialReference { get; set; } = "";
    public CredentialVerificationStatus CredentialVerificationStatus { get; set; }
    public DateTimeOffset? CredentialLastVerifiedAt { get; set; }
    public DeploymentHealth Health { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastVerificationAt { get; set; }
    public string VerificationMessage { get; set; } = "";
    public string? HostingProvider { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<EngineCapabilityEntity> Capabilities { get; set; } = [];
    public List<RuntimeControlEntity> Controls { get; set; } = [];
}

internal sealed class DeploymentSecretStoreEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? Description { get; set; }
    public DeploymentSecretStoreStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedByAccountId { get; set; }
    public List<DeploymentCredentialReferenceEntity> CredentialReferences { get; set; } = [];
}

internal sealed class DeploymentCredentialReferenceEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SecretStoreId { get; set; }
    public DeploymentSecretStoreEntity? SecretStore { get; set; }
    public string Name { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? Description { get; set; }
    public DeploymentSecretStoreStatus Status { get; set; }
    public CredentialVerificationStatus VerificationStatus { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedByAccountId { get; set; }
    public List<WorkflowEngineEntity> Engines { get; set; } = [];
}

internal sealed class WorkspaceDeploymentArtifactEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ArtifactId { get; set; } = "";
    public string LayoutVersion { get; set; } = "";
    public string ContentDigestAlgorithm { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public string EnvelopeVersion { get; set; } = "";
    public string ArtifactTypeId { get; set; } = "";
    public string ArtifactSchemaVersion { get; set; } = "";
    public string? ManifestDigestAlgorithm { get; set; }
    public string? ManifestDigest { get; set; }
    public string PayloadReferenceJson { get; set; } = "";
    public string ProducerJson { get; set; } = "";
    public string DisplayMetadataJson { get; set; } = "";
    public string CompatibilityHintsJson { get; set; } = "[]";
    public WorkspaceArtifactFormat Format { get; set; }
    public string ReferenceProvider { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? ManifestName { get; set; }
    public string? ManifestVersion { get; set; }
    public string? ManifestEnvironment { get; set; }
    public int ResourceCount { get; set; }
    public string ResourceSummaryJson { get; set; } = "[]";
    public WorkspaceArtifactChecksumStatus ChecksumStatus { get; set; }
    public WorkspaceArtifactInspectionStatus InspectionStatus { get; set; }
    public string DiagnosticsJson { get; set; } = "[]";
    public WorkspaceArtifactLifecycleStatus Status { get; set; } = WorkspaceArtifactLifecycleStatus.Active;
    public DateTimeOffset RegisteredAt { get; set; }
    public Guid RegisteredByAccountId { get; set; }
    public DateTimeOffset? LastInspectedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class WorkspaceArtifactUploadSessionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public WorkspaceArtifactUploadStatus Status { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? DeclaredSizeBytes { get; set; }
    public long? UploadedSizeBytes { get; set; }
    public string? StagedFilePath { get; set; }
    public string? IdempotencyKey { get; set; }
    public string DiagnosticsJson { get; set; } = "[]";
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? CompletedArtifactRecordId { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class EngineCapabilityEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EngineId { get; set; }
    public WorkflowEngineEntity? Engine { get; set; }
    public string CapabilityId { get; set; } = "";
    public string Label { get; set; } = "";
    public CapabilityBoundary Boundary { get; set; }
}

internal sealed class RuntimeControlEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EngineId { get; set; }
    public WorkflowEngineEntity? Engine { get; set; }
    public string ControlId { get; set; } = "";
    public string Label { get; set; } = "";
    public CapabilityBoundary Boundary { get; set; }
    public string RequiredCapabilityId { get; set; } = "";
    public string Description { get; set; } = "";
}

internal sealed class RuntimeControlExecutionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EngineId { get; set; }
    public WorkflowEngineEntity? Engine { get; set; }
    public Guid EnvironmentId { get; set; }
    public string ControlId { get; set; } = "";
    public string ControlLabel { get; set; } = "";
    public CapabilityBoundary Boundary { get; set; }
    public string RequiredCapabilityId { get; set; } = "";
    public Guid ConfirmationId { get; set; }
    public Guid ActorAccountId { get; set; }
    public RuntimeControlExecutionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Message { get; set; } = "";
}

internal sealed class DesiredStateRevisionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public int RevisionNumber { get; set; }
    public string Label { get; set; } = "";
    public string? Commit { get; set; }
    public string ContentHash { get; set; } = "";
    public string DesiredStateJson { get; set; } = "";
    public DateTimeOffset AuthoredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public List<StructuredDesiredStateRecordEntity> Records { get; set; } = [];
}

internal sealed class StructuredDesiredStateRecordEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RevisionId { get; set; }
    public DesiredStateRevisionEntity? Revision { get; set; }
    public DesiredStateRecordKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string ContentHash { get; set; } = "";
    public Guid? ArtifactRecordId { get; set; }
    public string? ArtifactId { get; set; }
    public string? ArtifactTypeId { get; set; }
    public string? ArtifactDigestAlgorithm { get; set; }
    public string? ArtifactDigest { get; set; }
}

internal sealed class WorkspacePermissionGrantEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AccountId { get; set; }
    public string Permission { get; set; } = "";
    public Guid? GrantedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class ActionConfirmationEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public ConfirmationActionType ActionType { get; set; }
    public string TargetId { get; set; } = "";
    public Guid ConfirmedByAccountId { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

internal sealed class DeploymentRunEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public Guid EngineId { get; set; }
    public Guid SourceRevisionId { get; set; }
    public Guid? PreviousDeployedRevisionId { get; set; }
    public Guid? RollbackSourceRunId { get; set; }
    public WorkspaceDeploymentRunStatus Status { get; set; }
    public DeploymentValidationOutcome ValidationOutcome { get; set; }
    public Guid ConfirmationId { get; set; }
    public Guid ActorAccountId { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? WorkerId { get; set; }
    public DateTimeOffset? WorkerHeartbeatAt { get; set; }
    public int AttemptNumber { get; set; }
    public string? RecoveryReason { get; set; }
    public string? FailureMessage { get; set; }
    public List<DeploymentRunHistoryEventEntity> History { get; set; } = [];
}

internal sealed class DeploymentRunHistoryEventEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RunId { get; set; }
    public DeploymentRunEntity? Run { get; set; }
    public WorkspaceDeploymentRunStatus Status { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class DeploymentCommandEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RunId { get; set; }
    public DeploymentRunEntity? Run { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid EngineId { get; set; }
    public DeploymentCommandAction Action { get; set; }
    public DeploymentCommandStatus Status { get; set; }
    public string ArtifactJson { get; set; } = "";
    public Guid? RevisionId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string? WorkerId { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public int AttemptNumber { get; set; }
    public int? PercentComplete { get; set; }
    public string? ProgressMessage { get; set; }
    public string? ObservedArtifactDigestAlgorithm { get; set; }
    public string? ObservedArtifactDigest { get; set; }
    public string? RuntimeReference { get; set; }
    public string DiagnosticsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AvailableAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DeploymentCommandEventEntity> Events { get; set; } = [];
}

internal sealed class DeploymentCommandEventEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid CommandId { get; set; }
    public DeploymentCommandEntity? Command { get; set; }
    public Guid RunId { get; set; }
    public DeploymentCommandStatus Status { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class DeploymentCommandWebhookNotificationEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EngineId { get; set; }
    public Guid CommandId { get; set; }
    public WebhookNotificationStatus Status { get; set; }
    public string SafePayloadJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

internal sealed class ObservabilityBindingEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public Guid? EngineId { get; set; }
    public ObservabilityBindingKind Kind { get; set; }
    public string Provider { get; set; } = "";
    public ObservabilityBindingStatus Status { get; set; }
    public string Scope { get; set; } = "";
    public Guid? CorrelatedRevisionId { get; set; }
    public string? Sample { get; set; }
}

internal sealed class DriftReportItemEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public Guid EngineId { get; set; }
    public string Area { get; set; } = "";
    public string Desired { get; set; } = "";
    public string Observed { get; set; } = "";
    public DriftAction Action { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}
