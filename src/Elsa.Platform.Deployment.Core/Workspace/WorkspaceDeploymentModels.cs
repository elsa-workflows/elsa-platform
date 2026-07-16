using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed record WorkspaceDeploymentApplication(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CreatedByAccountId,
    Guid? UpdatedByAccountId);

public sealed record WorkspaceDeploymentEnvironment(
    Guid Id,
    Guid WorkspaceId,
    Guid ApplicationId,
    string Name,
    EnvironmentTier Tier,
    Guid? DesiredRevisionId,
    Guid? DeployedRevisionId,
    DeploymentStatus DeploymentStatus,
    DriftStatus DriftStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? TierId = null,
    DeploymentTierProfile? TierDefinition = null);

public sealed record WorkspaceWorkflowEngine(
    Guid Id,
    Guid WorkspaceId,
    Guid EnvironmentId,
    string Name,
    string BaseUrl,
    string? Region,
    string? Version,
    CertificateStatus CertificateStatus,
    string CredentialProvider,
    string CredentialReference,
    CredentialVerificationStatus CredentialVerificationStatus,
    DateTimeOffset? CredentialLastVerifiedAt,
    DeploymentHealth Health,
    DateTimeOffset? LastHeartbeatAt,
    string? HostingProvider,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastVerificationAt = null,
    string VerificationMessage = "",
    Guid? CredentialReferenceId = null,
    EngineCredentialAssignmentStatus CredentialAssignmentStatus = EngineCredentialAssignmentStatus.Assigned);

public enum DeploymentSecretStoreStatus
{
    Active,
    Archived
}

public enum DeploymentSecretStoreType
{
    LocalEncryptedDatabase,
    AzureKeyVault,
    KubernetesSecrets,
    EnvironmentVariableName,
    GenericExternalReference
}

public enum EngineCredentialAssignmentStatus
{
    Assigned,
    Deferred
}

public sealed record WorkspaceDeploymentSecretStore(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Provider,
    DeploymentSecretStoreType Type,
    string? Description,
    DeploymentSecretStoreStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CreatedByAccountId,
    Guid? UpdatedByAccountId,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByAccountId);

public sealed record WorkspaceDeploymentCredentialReference(
    Guid Id,
    Guid WorkspaceId,
    Guid SecretStoreId,
    string SecretStoreName,
    string SecretStoreProvider,
    DeploymentSecretStoreType SecretStoreType,
    string Name,
    string Reference,
    string? Description,
    DeploymentSecretStoreStatus Status,
    CredentialVerificationStatus VerificationStatus,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CreatedByAccountId,
    Guid? UpdatedByAccountId,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByAccountId,
    bool HasProtectedSecret,
    int UsageCount);

public sealed record WorkspaceEngineCredentialSecret(
    Guid EngineId,
    Guid CredentialReferenceId,
    DeploymentSecretStoreStatus SecretStoreStatus,
    DeploymentSecretStoreStatus CredentialReferenceStatus,
    DeploymentSecretStoreType SecretStoreType,
    string? ProtectedSecret);

public sealed record WorkspaceDeploymentCredentialSecret(
    Guid CredentialReferenceId,
    DeploymentSecretStoreStatus SecretStoreStatus,
    DeploymentSecretStoreStatus CredentialReferenceStatus,
    DeploymentSecretStoreType SecretStoreType,
    string? ProtectedSecret);

public sealed record WorkspaceDeploymentCredentialUsage(
    Guid EngineId,
    string EngineName,
    Guid ApplicationId,
    string ApplicationName,
    Guid EnvironmentId,
    string EnvironmentName);

public sealed record WorkspaceEngineCapability(
    Guid Id,
    Guid WorkspaceId,
    Guid EngineId,
    string CapabilityId,
    string Label,
    CapabilityBoundary Boundary);

public sealed record WorkspaceRuntimeControlDefinition(
    Guid Id,
    Guid WorkspaceId,
    Guid EngineId,
    string ControlId,
    string Label,
    CapabilityBoundary Boundary,
    string RequiredCapabilityId,
    string Description);

public sealed record WorkspaceDesiredStateRevision(
    Guid Id,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId,
    int RevisionNumber,
    string Label,
    string? Commit,
    string ContentHash,
    string DesiredStateJson,
    DateTimeOffset AuthoredAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByAccountId);

public sealed record WorkspaceDesiredStateRevisionSummary(
    WorkspaceDesiredStateRevision Revision,
    string EnvironmentName,
    EnvironmentTier EnvironmentTier,
    Guid? EnvironmentTierId,
    string? EnvironmentTierName,
    bool IsCurrentDesired,
    bool IsCurrentDeployed,
    WorkspaceDeploymentRunStatus? LatestRunStatus,
    DateTimeOffset? LatestRunQueuedAt);

public sealed record WorkspaceDesiredStateRevisionDetail(
    WorkspaceDesiredStateRevisionSummary Summary,
    IReadOnlyList<WorkspaceDesiredStateRevisionRecord> Records,
    IReadOnlyList<WorkspaceDesiredStateRevisionRunSummary> Runs);

public sealed record WorkspaceDesiredStateRevisionRecord(
    Guid Id,
    DesiredStateRecordKind Kind,
    string Name,
    string PayloadJson,
    string ContentHash,
    Guid? ArtifactRecordId,
    string? ArtifactId,
    string? ArtifactTypeId,
    WorkspaceArtifactDigest? ArtifactDigest);

public sealed record WorkspaceDesiredStateRevisionRunSummary(
    Guid Id,
    Guid EnvironmentId,
    Guid EngineId,
    WorkspaceDeploymentRunStatus Status,
    DeploymentValidationOutcome ValidationOutcome,
    DateTimeOffset QueuedAt,
    DateTimeOffset? CompletedAt,
    string? FailureMessage);

public enum DesiredStateRequirementApplicability
{
    CurrentTier,
    ContextualFix,
    Optional
}

public sealed record DesiredStateRequirement(
    string Id,
    string? CapabilityId,
    string RecordKind,
    string Label,
    string Description,
    string ValidationId,
    bool Required,
    DesiredStateRequirementApplicability Applicability);

public sealed record WorkspacePromotionPreviewRequest(
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    Guid SourceRevisionId,
    Guid TargetEngineId);

public sealed record WorkspacePromotionRequest(
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    Guid SourceRevisionId,
    Guid TargetEngineId,
    string Label,
    string? Commit,
    Guid? ActorAccountId);

public sealed record WorkspacePromotionResult(
    WorkspaceDesiredStateRevision SourceRevision,
    WorkspaceDesiredStateRevision TargetRevision,
    PromotionComparison Comparison);

public sealed record CreateWorkflowApplicationRequest(string Name, string? Description, Guid? ActorAccountId);

public sealed record UpdateWorkflowApplicationRequest(string Name, string? Description, Guid? ActorAccountId);

public sealed record CreateDeploymentEnvironmentRequest(Guid ApplicationId, string Name, EnvironmentTier Tier, Guid? TierId = null);

public sealed record UpdateDeploymentEnvironmentRequest(Guid ApplicationId, string Name, EnvironmentTier Tier, Guid? TierId = null);

public sealed record RegisterWorkflowEngineRequest(
    Guid EnvironmentId,
    string Name,
    string BaseUrl,
    string? Region,
    string? CredentialProvider,
    string? CredentialReference,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider,
    Guid? CredentialReferenceId = null,
    EngineCredentialAssignmentStatus CredentialAssignmentStatus = EngineCredentialAssignmentStatus.Assigned);

public sealed record UpdateWorkflowEngineRequest(
    string Name,
    string BaseUrl,
    string? Region,
    string? CredentialProvider,
    string? CredentialReference,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider,
    Guid? CredentialReferenceId = null,
    EngineCredentialAssignmentStatus CredentialAssignmentStatus = EngineCredentialAssignmentStatus.Assigned);

public sealed record CreateDeploymentSecretStoreRequest(
    string Name,
    string? Provider,
    string? Description,
    Guid? ActorAccountId,
    DeploymentSecretStoreType Type = DeploymentSecretStoreType.GenericExternalReference);

public sealed record UpdateDeploymentSecretStoreRequest(
    string Name,
    string? Provider,
    string? Description,
    Guid? ActorAccountId,
    DeploymentSecretStoreType Type = DeploymentSecretStoreType.GenericExternalReference);

public sealed record CreateDeploymentCredentialReferenceRequest(
    Guid SecretStoreId,
    string Name,
    string Reference,
    string? Description,
    Guid? ActorAccountId,
    string? ProtectedSecret = null);

public sealed record UpdateDeploymentCredentialReferenceRequest(
    string Name,
    string Reference,
    string? Description,
    Guid? ActorAccountId,
    string? ProtectedSecret = null);

public sealed record RotateDeploymentCredentialReferenceRequest(
    string ProtectedSecret,
    Guid? ActorAccountId);

public sealed record CreateDesiredStateRevisionRequest(
    Guid ApplicationId,
    Guid EnvironmentId,
    string Label,
    string? Commit,
    string DesiredStateJson,
    Guid? ActorAccountId);

public sealed record WorkspaceDeploymentRunRequest(
    Guid SourceRevisionId,
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
    Guid ActorAccountId,
    DeploymentRunMode Mode);

public enum DeploymentRunMode
{
    DryRun,
    Apply
}
