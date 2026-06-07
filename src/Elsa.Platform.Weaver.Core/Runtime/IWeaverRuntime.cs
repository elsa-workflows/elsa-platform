using Elsa.Platform.Weaver.Core.Sessions;

namespace Elsa.Platform.Weaver.Core.Runtime;

public interface IWeaverRuntime
{
    IAsyncEnumerable<WeaverRuntimeEvent> SendAsync(
        WeaverRuntimeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WeaverRuntimeRequest(
    Guid SessionId,
    Guid WorkspaceId,
    Guid AccountId,
    string Prompt,
    WeaverMode Mode,
    string? RoutePath,
    IReadOnlyDictionary<string, string> Context);

public abstract record WeaverRuntimeEvent(DateTimeOffset CreatedAt);

public sealed record WeaverAssistantDeltaEvent(string Content, DateTimeOffset CreatedAt) : WeaverRuntimeEvent(CreatedAt);

public sealed record WeaverToolStartedEvent(string ToolName, DateTimeOffset CreatedAt) : WeaverRuntimeEvent(CreatedAt);

public sealed record WeaverToolCompletedEvent(string ToolName, string Summary, DateTimeOffset CreatedAt) : WeaverRuntimeEvent(CreatedAt);

public sealed record WeaverPlanCreatedEvent(Guid PlanId, string Title, DateTimeOffset CreatedAt) : WeaverRuntimeEvent(CreatedAt);

public sealed record WeaverRuntimeErrorEvent(string Message, DateTimeOffset CreatedAt) : WeaverRuntimeEvent(CreatedAt);

public sealed record WeaverRuntimeIdleEvent(DateTimeOffset CreatedAt) : WeaverRuntimeEvent(CreatedAt);
