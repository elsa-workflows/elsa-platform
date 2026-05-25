using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

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

public enum ConfirmationActionType
{
    Deploy,
    Rollback,
    RuntimeControl
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
