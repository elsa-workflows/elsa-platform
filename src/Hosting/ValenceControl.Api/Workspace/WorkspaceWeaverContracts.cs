using ValenceControl.Weaver.Core.Configuration;
using ValenceControl.Weaver.Core.Sessions;

namespace ValenceControl.Api.Workspace;

public sealed record WorkspaceWeaverConfigurationResponse(
    bool Enabled,
    WeaverProviderMode ProviderMode,
    string Model,
    string? ReasoningEffort,
    bool StreamingEnabled,
    IReadOnlyList<WeaverMode> Modes,
    string? DisabledReason);

public sealed record WorkspaceWeaverCreateSessionRequest(
    string? RoutePath,
    WeaverMode Mode,
    IReadOnlyDictionary<string, string>? Context);

public sealed record WorkspaceWeaverSessionResponse(
    Guid Id,
    WeaverSessionStatus Status,
    WeaverMode Mode,
    DateTimeOffset CreatedAt);

public sealed record WorkspaceWeaverSendMessageRequest(
    string Prompt,
    WeaverMode Mode,
    string? Delivery);

public sealed record WorkspaceWeaverSendMessageResponse(
    Guid MessageId,
    Guid? AssistantMessageId,
    WeaverSessionStatus SessionStatus);

public sealed record WorkspaceWeaverSessionDetailResponse(
    WorkspaceWeaverSessionResponse Session,
    IReadOnlyList<WorkspaceWeaverMessageResponse> Messages,
    IReadOnlyList<WorkspaceWeaverToolCallResponse> ToolCalls,
    IReadOnlyList<WorkspaceWeaverPlanResponse> Plans);

public sealed record WorkspaceWeaverMessageResponse(
    Guid Id,
    WeaverMessageRole Role,
    string Content,
    WeaverRedactionState RedactionState,
    int Sequence,
    DateTimeOffset CreatedAt);

public sealed record WorkspaceWeaverToolCallResponse(
    Guid Id,
    string ToolName,
    string? ResultSummaryJson,
    WeaverToolAuthorizationResult AuthorizationResult,
    WeaverToolCallStatus Status,
    long? DurationMilliseconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record WorkspaceWeaverPlanResponse(
    Guid Id,
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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceWeaverEventResponse(string Type, string? Content);

public sealed record WorkspaceWeaverPlanApprovalRequest(
    int Version,
    WeaverPlanApprovalDecision Decision,
    Guid? ConfirmationId,
    string? Reason);

public sealed record WorkspaceWeaverPlanApprovalResponse(
    Guid PlanId,
    int Version,
    WeaverPlanStatus Status);

public sealed record WorkspaceWeaverPlanExecuteRequest(int Version);

public sealed record WorkspaceWeaverPlanExecuteResponse(
    Guid ExecutionId,
    WeaverPlanExecutionStatus Status,
    string LinkedResourceJson);
