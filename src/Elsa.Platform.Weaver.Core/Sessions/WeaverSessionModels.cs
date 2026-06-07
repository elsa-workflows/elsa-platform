using System.Text.Json;
using Elsa.Platform.Weaver.Core.Configuration;

namespace Elsa.Platform.Weaver.Core.Sessions;

public sealed record WeaverSession(
    Guid Id,
    Guid WorkspaceId,
    Guid? OrganizationId,
    Guid AccountId,
    string? CopilotSessionId,
    string? RoutePath,
    JsonDocument? Context,
    WeaverMode Mode,
    WeaverProviderMode ProviderMode,
    string Model,
    string? ReasoningEffort,
    WeaverSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record WeaverMessage(
    Guid Id,
    Guid SessionId,
    WeaverMessageRole Role,
    string Content,
    WeaverRedactionState RedactionState,
    int Sequence,
    DateTimeOffset CreatedAt);

public sealed record WeaverToolCall(
    Guid Id,
    Guid SessionId,
    string ToolName,
    string? ArgumentsJson,
    string? ArgumentsHash,
    string? ResultSummaryJson,
    WeaverToolAuthorizationResult AuthorizationResult,
    WeaverToolCallStatus Status,
    long? DurationMilliseconds,
    string? TraceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record WeaverPlan(
    Guid Id,
    Guid SessionId,
    int Version,
    WeaverPlanType PlanType,
    string Title,
    string Summary,
    string TargetJson,
    string ImpactJson,
    string ValidationJson,
    string? RollbackJson,
    WeaverPlanRisk Risk,
    WeaverPlanStatus Status,
    Guid CreatedByAccountId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WeaverPlanApproval(
    Guid Id,
    Guid PlanId,
    int PlanVersion,
    Guid AccountId,
    WeaverPlanApprovalDecision Decision,
    string? PermissionSnapshotJson,
    Guid? ConfirmationId,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record WeaverPlanExecution(
    Guid Id,
    Guid PlanId,
    int PlanVersion,
    WeaverPlanExecutionStatus Status,
    string LinkedResourceJson,
    string? DiagnosticsJson,
    string? TraceId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public enum WeaverMode
{
    Inspect,
    Plan,
    Operate
}

public enum WeaverSessionStatus
{
    Active,
    WaitingForUser,
    WaitingForApproval,
    Executing,
    Completed,
    Failed,
    Canceled,
    Archived
}

public enum WeaverMessageRole
{
    User,
    Assistant,
    System,
    Tool
}

public enum WeaverRedactionState
{
    None,
    Redacted,
    Omitted
}

public enum WeaverToolAuthorizationResult
{
    Allowed,
    Denied,
    RequiresApproval
}

public enum WeaverToolCallStatus
{
    Started,
    Succeeded,
    Failed,
    Denied,
    Canceled
}

public enum WeaverPlanType
{
    Deployment,
    Promotion,
    Rollback,
    RuntimeControl,
    EngineRegistration,
    SecretReference,
    SetupGuidance
}

public enum WeaverPlanRisk
{
    Low,
    Medium,
    High
}

public enum WeaverPlanStatus
{
    Draft,
    Blocked,
    ReadyForApproval,
    Approved,
    Rejected,
    Executing,
    Succeeded,
    Failed,
    Canceled
}

public enum WeaverPlanApprovalDecision
{
    Approved,
    Rejected
}

public enum WeaverPlanExecutionStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,
    Compensated
}
