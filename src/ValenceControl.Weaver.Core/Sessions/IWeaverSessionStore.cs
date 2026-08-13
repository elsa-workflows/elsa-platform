namespace ValenceControl.Weaver.Core.Sessions;

public interface IWeaverSessionStore
{
    Task<WeaverSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default);

    Task<WeaverSession?> GetSessionAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeaverMessage>> ListMessagesAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<WeaverMessage> AddMessageAsync(Guid workspaceId, WeaverMessage message, CancellationToken cancellationToken = default);

    Task<WeaverToolCall> AddToolCallAsync(Guid workspaceId, WeaverToolCall toolCall, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeaverToolCall>> ListToolCallsAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<WeaverPlan> AddPlanAsync(Guid workspaceId, WeaverPlan plan, CancellationToken cancellationToken = default);

    Task<WeaverPlan?> GetPlanAsync(Guid workspaceId, Guid planId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeaverPlan>> ListPlansAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<WeaverPlan> UpdatePlanStatusAsync(Guid workspaceId, Guid planId, int version, WeaverPlanStatus status, CancellationToken cancellationToken = default);

    Task<WeaverPlanApproval> AddPlanApprovalAsync(Guid workspaceId, WeaverPlanApproval approval, CancellationToken cancellationToken = default);

    Task<WeaverPlanExecution?> GetPlanExecutionAsync(Guid workspaceId, Guid planId, int planVersion, CancellationToken cancellationToken = default);

    Task<WeaverPlanExecution> AddPlanExecutionAsync(Guid workspaceId, WeaverPlanExecution execution, CancellationToken cancellationToken = default);
}
