namespace Elsa.Platform.Deployment.Core.Workspace;

public enum DeploymentCommandAction
{
    Deploy,
    Rollback,
    Validate,
    RuntimeControl
}

public enum DeploymentCommandStatus
{
    Pending,
    Claimed,
    Running,
    Completed,
    Failed,
    Rejected,
    Cancelled,
    RecoveryRequired,
    Expired
}

public enum DeploymentCommandDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum WebhookNotificationStatus
{
    Pending,
    Sent,
    Failed,
    Skipped
}

public sealed record DeploymentCommandArtifactReference(
    Guid? ArtifactRecordId,
    string? ArtifactId,
    string? ArtifactTypeId,
    WorkspaceArtifactDigest? ContentDigest);

public sealed record DeploymentCommandRevisionReference(Guid? RevisionId);

public sealed record DeploymentCommandDiagnostic(
    string Code,
    DeploymentCommandDiagnosticSeverity Severity,
    string Message);

public sealed record DeploymentCommand(
    Guid Id,
    Guid WorkspaceId,
    Guid RunId,
    Guid EnvironmentId,
    Guid EngineId,
    DeploymentCommandAction Action,
    DeploymentCommandStatus Status,
    DeploymentCommandArtifactReference? Artifact,
    DeploymentCommandRevisionReference? Revision,
    string IdempotencyKey,
    string? WorkerId,
    string? LeaseToken,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? HeartbeatAt,
    int AttemptNumber,
    int? PercentComplete,
    string? ProgressMessage,
    WorkspaceArtifactDigest? ObservedArtifactDigest,
    string? RuntimeReference,
    IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AvailableAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CompletedAt);

public sealed record DeploymentCommandEvent(
    Guid Id,
    Guid WorkspaceId,
    Guid CommandId,
    Guid RunId,
    DeploymentCommandStatus Status,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record DeploymentCommandWebhookNotification(
    Guid Id,
    Guid WorkspaceId,
    Guid EngineId,
    Guid CommandId,
    WebhookNotificationStatus Status,
    string SafePayloadJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public sealed record CreateDeploymentCommandRequest(
    Guid RunId,
    Guid EnvironmentId,
    Guid EngineId,
    DeploymentCommandAction Action,
    DeploymentCommandArtifactReference? Artifact,
    DeploymentCommandRevisionReference? Revision,
    string IdempotencyKey,
    DateTimeOffset? AvailableAt = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record ClaimDeploymentCommandRequest(
    Guid EngineId,
    string WorkerId,
    TimeSpan LeaseDuration);

public sealed record DeploymentCommandClaim(
    DeploymentCommand Command,
    string LeaseToken);

public sealed record DeploymentCommandHeartbeatRequest(
    string LeaseToken,
    string WorkerId);

public sealed record DeploymentCommandProgressRequest(
    string LeaseToken,
    string Status,
    int? PercentComplete,
    string Message);

public sealed record CompleteDeploymentCommandRequest(
    string LeaseToken,
    WorkspaceArtifactDigest? ObservedArtifactDigest,
    string? RuntimeReference,
    IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics);

public sealed record FailDeploymentCommandRequest(
    string LeaseToken,
    IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics);

public sealed record RejectDeploymentCommandRequest(
    string LeaseToken,
    IReadOnlyList<DeploymentCommandDiagnostic> Diagnostics);
