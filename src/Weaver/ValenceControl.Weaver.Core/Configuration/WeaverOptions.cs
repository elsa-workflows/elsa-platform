namespace ValenceControl.Weaver.Core.Configuration;

public sealed class WeaverOptions
{
    public bool Enabled { get; set; }

    public WeaverProviderMode ProviderMode { get; set; } = WeaverProviderMode.Disabled;

    public string Model { get; set; } = "gpt-5";

    public string? ReasoningEffort { get; set; } = "medium";

    public WeaverProviderOptions Provider { get; set; } = new();

    public WeaverRuntimeOptions Runtime { get; set; } = new();

    public WeaverTelemetryOptions Telemetry { get; set; } = new();

    public bool IsAvailable(out string? disabledReason)
    {
        if (!Enabled)
        {
            disabledReason = "Weaver is disabled.";
            return false;
        }

        if (ProviderMode == WeaverProviderMode.Disabled)
        {
            disabledReason = "Weaver provider mode is disabled.";
            return false;
        }

        if (ProviderMode == WeaverProviderMode.BringYourOwnKey && string.IsNullOrWhiteSpace(Provider.ApiKeyEnvironmentVariable))
        {
            disabledReason = "Weaver BYOK provider requires an API key environment variable.";
            return false;
        }

        disabledReason = null;
        return true;
    }
}

public enum WeaverProviderMode
{
    Disabled,
    GitHubCopilot,
    BringYourOwnKey,
    Fake
}

public sealed class WeaverProviderOptions
{
    public string Type { get; set; } = "openai";

    public string? BaseUrl { get; set; }

    public string? ApiKeyEnvironmentVariable { get; set; }

    public string? GitHubTokenEnvironmentVariable { get; set; }
}

public sealed class WeaverRuntimeOptions
{
    public string? CopilotHome { get; set; }

    public int TurnTimeoutSeconds { get; set; } = 120;

    public int MaxConcurrentSessions { get; set; } = 4;

    public int ToolResultMaxBytes { get; set; } = 20_000;
}

public sealed class WeaverTelemetryOptions
{
    public bool Enabled { get; set; }

    public string? OtlpEndpoint { get; set; }
}
