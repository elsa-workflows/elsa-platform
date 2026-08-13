using System.Text.Json;
using ValenceControl.Weaver.Core.Configuration;
using ValenceControl.Weaver.Core.Plans;
using ValenceControl.Weaver.Core.Runtime;
using ValenceControl.Weaver.Core.Safety;
using Microsoft.Extensions.Options;

namespace ValenceControl.Weaver.Core.Sessions;

public sealed class WeaverSessionService(
    IWeaverSessionStore store,
    IWeaverRuntime runtime,
    WeaverPlanService plans,
    WeaverRedactionService redaction,
    IOptions<WeaverOptions> options,
    TimeProvider timeProvider)
{
    private readonly WeaverOptions _options = options.Value;

    public WeaverAvailability GetAvailability()
    {
        var available = _options.IsAvailable(out var disabledReason);
        var modes = available
            ? [WeaverMode.Inspect, WeaverMode.Plan]
            : Array.Empty<WeaverMode>();

        return new WeaverAvailability(
            _options.Enabled,
            _options.ProviderMode,
            _options.Model,
            _options.ReasoningEffort,
            available,
            _options.ProviderMode is not WeaverProviderMode.Disabled,
            modes,
            disabledReason);
    }

    public async Task<WeaverSession> CreateSessionAsync(
        Guid workspaceId,
        Guid? organizationId,
        Guid accountId,
        CreateWeaverSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsAvailable(out var disabledReason))
            throw new InvalidOperationException(disabledReason);

        var now = timeProvider.GetUtcNow();
        var session = new WeaverSession(
            Guid.NewGuid(),
            workspaceId,
            organizationId,
            accountId,
            null,
            request.RoutePath,
            JsonSerializer.SerializeToDocument(request.Context),
            request.Mode,
            _options.ProviderMode,
            _options.Model,
            _options.ReasoningEffort,
            WeaverSessionStatus.Active,
            now,
            now,
            null);

        return await store.CreateSessionAsync(session, cancellationToken);
    }

    public async Task<WeaverMessageResult> SendMessageAsync(
        Guid workspaceId,
        Guid sessionId,
        Guid accountId,
        SendWeaverMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await store.GetSessionAsync(workspaceId, sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Weaver session does not exist in the workspace.");

        var messages = await store.ListMessagesAsync(workspaceId, sessionId, cancellationToken);
        var sequence = messages.Count == 0 ? 1 : messages.Max(x => x.Sequence) + 1;
        var prompt = redaction.Redact(request.Prompt);
        var userMessage = await store.AddMessageAsync(
            workspaceId,
            new WeaverMessage(
                Guid.NewGuid(),
                sessionId,
                WeaverMessageRole.User,
                prompt.Value,
                prompt.Redacted ? WeaverRedactionState.Redacted : WeaverRedactionState.None,
                sequence,
                timeProvider.GetUtcNow()),
            cancellationToken);

        var assistantContent = new List<string>();
        var toolStarts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        await foreach (var runtimeEvent in runtime.SendAsync(ToRuntimeRequest(session, userMessage.Content, request.Mode, accountId), cancellationToken))
        {
            switch (runtimeEvent)
            {
                case WeaverAssistantDeltaEvent assistant:
                    assistantContent.Add(assistant.Content);
                    break;
                case WeaverToolStartedEvent started:
                    toolStarts[started.ToolName] = started.CreatedAt;
                    break;
                case WeaverToolCompletedEvent completed:
                    var createdAt = toolStarts.GetValueOrDefault(completed.ToolName, completed.CreatedAt);
                    await store.AddToolCallAsync(
                        workspaceId,
                        new WeaverToolCall(
                            Guid.NewGuid(),
                            sessionId,
                            completed.ToolName,
                            null,
                            null,
                            JsonSerializer.Serialize(new { summary = redaction.Redact(completed.Summary).Value }),
                            WeaverToolAuthorizationResult.Allowed,
                            WeaverToolCallStatus.Succeeded,
                            (long)Math.Max(0, (completed.CreatedAt - createdAt).TotalMilliseconds),
                            null,
                            createdAt,
                            completed.CreatedAt),
                        cancellationToken);
                    break;
                case WeaverRuntimeErrorEvent error:
                    assistantContent.Add($"Weaver runtime error: {redaction.Redact(error.Message).Value}");
                    break;
            }
        }

        WeaverMessage? assistantMessage = null;
        var content = string.Join("", assistantContent).Trim();
        if (!string.IsNullOrWhiteSpace(content))
        {
            var assistantRedaction = redaction.Redact(content);
            assistantMessage = await store.AddMessageAsync(
                workspaceId,
                new WeaverMessage(
                    Guid.NewGuid(),
                    sessionId,
                    WeaverMessageRole.Assistant,
                    assistantRedaction.Value,
                    assistantRedaction.Redacted ? WeaverRedactionState.Redacted : WeaverRedactionState.None,
                    sequence + 1,
                    timeProvider.GetUtcNow()),
                cancellationToken);
        }

        if (plans.ShouldDraftPlan(request.Mode, userMessage.Content))
            await plans.DraftPlanAsync(workspaceId, sessionId, accountId, userMessage.Content, cancellationToken);

        return new WeaverMessageResult(userMessage.Id, assistantMessage?.Id, WeaverSessionStatus.Active);
    }

    public async Task<WeaverSessionDetail> GetSessionDetailAsync(Guid workspaceId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await store.GetSessionAsync(workspaceId, sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Weaver session does not exist in the workspace.");

        var messages = await store.ListMessagesAsync(workspaceId, sessionId, cancellationToken);
        var toolCalls = await store.ListToolCallsAsync(workspaceId, sessionId, cancellationToken);
        var plans = await store.ListPlansAsync(workspaceId, sessionId, cancellationToken);

        return new WeaverSessionDetail(session, messages, toolCalls, plans);
    }

    private static WeaverRuntimeRequest ToRuntimeRequest(WeaverSession session, string prompt, WeaverMode mode, Guid accountId) => new(
        session.Id,
        session.WorkspaceId,
        accountId,
        prompt,
        mode,
        session.RoutePath,
        ToContextDictionary(session.Context));

    private static IReadOnlyDictionary<string, string> ToContextDictionary(JsonDocument? context)
    {
        if (context is null || context.RootElement.ValueKind is not JsonValueKind.Object)
            return new Dictionary<string, string>();

        return context.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText(),
                StringComparer.Ordinal);
    }
}

public sealed record WeaverAvailability(
    bool Enabled,
    WeaverProviderMode ProviderMode,
    string Model,
    string? ReasoningEffort,
    bool Available,
    bool StreamingEnabled,
    IReadOnlyList<WeaverMode> Modes,
    string? DisabledReason);

public sealed record CreateWeaverSessionRequest(string? RoutePath, WeaverMode Mode, IReadOnlyDictionary<string, string> Context);

public sealed record SendWeaverMessageRequest(string Prompt, WeaverMode Mode);

public sealed record WeaverMessageResult(Guid MessageId, Guid? AssistantMessageId, WeaverSessionStatus SessionStatus);

public sealed record WeaverSessionDetail(
    WeaverSession Session,
    IReadOnlyList<WeaverMessage> Messages,
    IReadOnlyList<WeaverToolCall> ToolCalls,
    IReadOnlyList<WeaverPlan> Plans);
