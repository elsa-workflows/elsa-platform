using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ElsaControl.Weaver.Core.Configuration;
using ElsaControl.Weaver.Core.Tools;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElsaControl.Weaver.Core.Runtime;

public sealed class CopilotWeaverRuntime(
    IOptions<WeaverOptions> options,
    WeaverWorkspaceTools workspaceTools,
    ILogger<CopilotWeaverRuntime> logger) : IWeaverRuntime
{
    private const string CurrentContextToolName = "weaver_get_current_context";

    public async IAsyncEnumerable<WeaverRuntimeEvent> SendAsync(
        WeaverRuntimeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var channel = Channel.CreateUnbounded<WeaverRuntimeEvent>();

        await using var client = new CopilotClient(CreateClientOptions(settings));

        CopilotSession? session = null;
        IDisposable? subscription = null;

        try
        {
            await client.StartAsync(cancellationToken);

            session = await OpenSessionAsync(client, request, settings, cancellationToken);
            subscription = session.On<SessionEvent>(evt => PublishEvent(channel.Writer, evt));

            await session.SendAsync(new MessageOptions { Prompt = request.Prompt }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Weaver Copilot runtime failed for session {SessionId}", request.SessionId);
            channel.Writer.TryWrite(new WeaverRuntimeErrorEvent("The Copilot runtime failed to process this turn.", DateTimeOffset.UtcNow));
            channel.Writer.TryWrite(new WeaverRuntimeIdleEvent(DateTimeOffset.UtcNow));
        }

        await foreach (var runtimeEvent in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return runtimeEvent;

            if (runtimeEvent is WeaverRuntimeIdleEvent)
                break;
        }

        subscription?.Dispose();

        if (session is not null)
            await session.DisposeAsync();
    }

    private async Task<CopilotSession> OpenSessionAsync(
        CopilotClient client,
        WeaverRuntimeRequest request,
        WeaverOptions settings,
        CancellationToken cancellationToken)
    {
        var sessionId = request.SessionId.ToString("N");
        var resumeConfig = CreateResumeSessionConfig(request, settings);

        try
        {
            return await client.ResumeSessionAsync(sessionId, resumeConfig, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Creating new Copilot session {SessionId} for Weaver", sessionId);
        }

        return await client.CreateSessionAsync(CreateSessionConfig(sessionId, request, settings), cancellationToken);
    }

    private CopilotClientOptions CreateClientOptions(WeaverOptions settings)
    {
        var copilotHome = ResolveCopilotHome(settings);
        var environment = new Dictionary<string, string>();
        var githubToken = ReadEnvironmentSecret(settings.Provider.GitHubTokenEnvironmentVariable);

        environment["COPILOT_HOME"] = copilotHome;

        return new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = copilotHome,
            Environment = environment,
            GitHubToken = githubToken,
            UseLoggedInUser = string.IsNullOrWhiteSpace(githubToken) ? null : false,
            SessionIdleTimeoutSeconds = settings.Runtime.TurnTimeoutSeconds,
            Telemetry = settings.Telemetry.Enabled
                ? new TelemetryConfig
                {
                    OtlpEndpoint = settings.Telemetry.OtlpEndpoint,
                    SourceName = "ElsaControl.Weaver",
                    CaptureContent = false
                }
                : null,
            Logger = logger
        };
    }

    private SessionConfig CreateSessionConfig(string sessionId, WeaverRuntimeRequest request, WeaverOptions settings)
    {
        var config = CreateSessionConfigBase<SessionConfig>(request, settings);
        config.SessionId = sessionId;
        return config;
    }

    private ResumeSessionConfig CreateResumeSessionConfig(WeaverRuntimeRequest request, WeaverOptions settings)
    {
        return CreateSessionConfigBase<ResumeSessionConfig>(request, settings);
    }

    private TConfig CreateSessionConfigBase<TConfig>(WeaverRuntimeRequest request, WeaverOptions settings)
        where TConfig : SessionConfigBase, new()
    {
        var config = new TConfig
        {
            ClientName = "Elsa Control Weaver",
            Model = settings.Model,
            ReasoningEffort = settings.ReasoningEffort,
            Streaming = true,
            IncludeSubAgentStreamingEvents = true,
            SkipCustomInstructions = true,
            EnableConfigDiscovery = false,
            EnableFileHooks = false,
            EnableHostGitOperations = false,
            EnableSkills = false,
            EnableSessionStore = false,
            WorkingDirectory = ResolveCopilotHome(settings),
            InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
            Tools = [CreateCurrentContextTool(request)],
            AvailableTools = new ToolSet().AddCustom(CurrentContextToolName),
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = BuildSystemMessage(request)
            },
            OnPermissionRequest = (_, _) => Task.FromResult(PermissionDecision.Reject("Weaver only allows explicitly registered Elsa Control tools in this host."))
        };

        if (settings.ProviderMode == WeaverProviderMode.BringYourOwnKey)
            config.Provider = CreateProviderConfig(settings);

        return config;
    }

    private AIFunctionDeclaration CreateCurrentContextTool(WeaverRuntimeRequest request)
    {
        object GetCurrentContext(
            [Description("Optional note about the current console page.")] string? note = null)
        {
            var context = workspaceTools.GetCurrentContext(request.RoutePath, request.Context);

            return new
            {
                context.Summary,
                context.RoutePath,
                context.Items,
                Note = note
            };
        }

        return CopilotTool.DefineTool(
            (Func<string?, object>)GetCurrentContext,
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = CurrentContextToolName,
                Description = "Read the current Elsa Control workspace and console route context. This tool is read-only."
            });
    }

    private static ProviderConfig CreateProviderConfig(WeaverOptions settings)
    {
        var apiKey = ReadEnvironmentSecret(settings.Provider.ApiKeyEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Weaver BYOK provider is configured but the API key environment variable is not set.");

        var provider = new ProviderConfig
        {
            Type = settings.Provider.Type,
            ApiKey = apiKey,
            ModelId = settings.Model
        };

        if (!string.IsNullOrWhiteSpace(settings.Provider.BaseUrl))
            provider.BaseUrl = settings.Provider.BaseUrl;

        return provider;
    }

    private static string BuildSystemMessage(WeaverRuntimeRequest request) =>
        $"""
        You are Weaver, the Elsa Control operations agent for workspace {request.WorkspaceId}.
        Work in {request.Mode} mode.
        Use only Elsa Control tools exposed by this host.
        Do not use shell, filesystem, package manager, network, source-control, or generic editing tools.
        For any deployment, rollback, promotion, credential, package, runtime, tenant, or workspace mutation, explain the plan and require Elsa Control approval before execution.
        Never ask the user to paste secrets. Use provider-backed secret references only.
        Current console route: {request.RoutePath ?? "unknown"}.
        """;

    private static string? ReadEnvironmentSecret(string? variableName) =>
        string.IsNullOrWhiteSpace(variableName) ? null : Environment.GetEnvironmentVariable(variableName);

    private static string ResolveCopilotHome(WeaverOptions settings) =>
        string.IsNullOrWhiteSpace(settings.Runtime.CopilotHome)
            ? Path.GetFullPath(Path.Combine(".weaver", "copilot"))
            : settings.Runtime.CopilotHome;

    private static void PublishEvent(ChannelWriter<WeaverRuntimeEvent> writer, SessionEvent evt)
    {
        var createdAt = evt.Timestamp;

        switch (evt)
        {
            case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data.DeltaContent):
                writer.TryWrite(new WeaverAssistantDeltaEvent(delta.Data.DeltaContent, createdAt));
                break;
            case AssistantMessageEvent message when !string.IsNullOrWhiteSpace(message.Data.Content):
                writer.TryWrite(new WeaverAssistantDeltaEvent(message.Data.Content, createdAt));
                break;
            case ToolExecutionStartEvent started:
                writer.TryWrite(new WeaverToolStartedEvent(started.Data.ToolName ?? "tool", createdAt));
                break;
            case ToolExecutionCompleteEvent completed:
                writer.TryWrite(new WeaverToolCompletedEvent(completed.Data.ToolDescription?.Name ?? "tool", completed.Data.Success ? "Completed." : "Failed.", createdAt));
                break;
            case SessionErrorEvent error:
                writer.TryWrite(new WeaverRuntimeErrorEvent(error.Data.Message ?? "The Copilot runtime reported an error.", createdAt));
                break;
            case SessionIdleEvent:
                writer.TryWrite(new WeaverRuntimeIdleEvent(createdAt));
                writer.TryComplete();
                break;
        }
    }
}
