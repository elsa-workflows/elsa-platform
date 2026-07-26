using ValenceControl.Deployment.Core.Cockpit;

namespace ValenceControl.Deployment.Core.Workspace;

public sealed record ActionConfirmation(
    Guid Id,
    Guid WorkspaceId,
    ConfirmationActionType ActionType,
    string TargetId,
    Guid ConfirmedByAccountId,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt);

public sealed record CreateActionConfirmationRequest(
    ConfirmationActionType ActionType,
    string TargetId,
    Guid ConfirmedByAccountId,
    TimeSpan? Lifetime = null);

public sealed record ConfirmationConsumptionResult(
    ActionConfirmation? Confirmation,
    DeploymentValidation Validation)
{
    public bool Succeeded => Validation.Severity == ValidationSeverity.Pass;
}

public sealed record ConfirmationUseAttempt(
    ActionConfirmation Confirmation,
    bool Consumed);

public sealed record WorkspaceDeploymentRun(
    Guid Id,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid EngineId,
    Guid SourceRevisionId,
    Guid? PreviousDeployedRevisionId,
    Guid? RollbackSourceRunId,
    WorkspaceDeploymentRunStatus Status,
    DeploymentValidationOutcome ValidationOutcome,
    Guid ConfirmationId,
    Guid ActorAccountId,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    string? WorkerId,
    DateTimeOffset? WorkerHeartbeatAt,
    int AttemptNumber,
    string? RecoveryReason,
    string? FailureMessage);

public sealed record DeploymentRunHistoryEvent(
    Guid Id,
    Guid WorkspaceId,
    Guid RunId,
    WorkspaceDeploymentRunStatus Status,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record QueueWorkspaceDeploymentRunRequest(
    Guid SourceRevisionId,
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
    Guid ConfirmationId,
    Guid ActorAccountId,
    Guid? RollbackSourceRunId = null);

public sealed record WorkspaceDeploymentRunDetail(
    WorkspaceDeploymentRun Run,
    IReadOnlyList<DeploymentRunHistoryEvent> History,
    IReadOnlyList<DeploymentRunCommandSummary> Commands);

public sealed record DeploymentRunCommandSummary(
    Guid Id,
    Guid WorkspaceId,
    Guid RunId,
    Guid EnvironmentId,
    Guid EngineId,
    DeploymentCommandAction Action,
    DeploymentCommandStatus Status,
    DeploymentCommandArtifactReference? Artifact,
    string? WorkerId,
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
    DateTimeOffset? CompletedAt,
    IReadOnlyList<DeploymentCommandArtifactItem>? Artifacts = null);

public sealed record RuntimeControlExecutionRequest(
    Guid EngineId,
    string ControlId,
    Guid ConfirmationId,
    Guid ActorAccountId);

public sealed record RuntimeControlExecution(
    Guid Id,
    Guid WorkspaceId,
    Guid EngineId,
    Guid EnvironmentId,
    string ControlId,
    string ControlLabel,
    CapabilityBoundary Boundary,
    string RequiredCapabilityId,
    Guid ConfirmationId,
    Guid ActorAccountId,
    RuntimeControlExecutionStatus Status,
    DateTimeOffset CreatedAt,
    string Message);

public enum RuntimeControlExecutionStatus
{
    Succeeded,
    Failed
}

public enum ConfirmationActionType
{
    Deploy,
    Rollback,
    RuntimeControl,
    HealingEmergencyStop,
    HealingEmergencyResume,
    HealingAutomaticMerge,
    HealingRepairStop,
    HealingVerificationWaive
}

public enum WorkspaceDeploymentRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Blocked,
    Cancelled,
    RolledBack,
    RecoveryRequired
}
