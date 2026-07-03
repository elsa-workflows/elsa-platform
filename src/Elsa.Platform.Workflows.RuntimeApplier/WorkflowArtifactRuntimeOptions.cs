using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed record WorkflowArtifactRuntimeOptions
{
    public Uri? PlatformEndpoint { get; init; }

    public Guid? WorkspaceId { get; init; }

    public Guid? EngineId { get; init; }

    public string? EngineSecret { get; init; }

    public string WorkerId { get; init; } = $"{Environment.MachineName}:{Environment.ProcessId}";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ClaimLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LeaseSafetyMargin { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan PayloadRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxRetryAttempts { get; init; } = 3;

    public string RuntimeFamily { get; init; } = "elsa-workflows";

    public string? RuntimeVersion { get; init; }

    public IReadOnlyList<string> SupportedArtifactSchemaVersions { get; init; } =
        [ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion];

    public IReadOnlyList<string> Capabilities { get; init; } = ["workflow-definition.apply", "loom.recipe.apply"];

    public long MaxPayloadBytes { get; init; } = 4 * 1024 * 1024;

    public IReadOnlyList<string> AllowedPayloadReferenceProviders { get; init; } = ["producer-managed"];

    public IReadOnlyList<string> AllowedPayloadHosts { get; init; } = [];

    public void Validate()
    {
        if (PlatformEndpoint is null)
            throw new InvalidOperationException("Platform endpoint is required before runtime command sync can start.");
        if (WorkspaceId is null || WorkspaceId == Guid.Empty)
            throw new InvalidOperationException("Platform workspace is required before runtime command sync can start.");
        if (EngineId is null || EngineId == Guid.Empty)
            throw new InvalidOperationException("Platform engine is required before runtime command sync can start.");
        if (string.IsNullOrWhiteSpace(WorkerId))
            throw new InvalidOperationException("Runtime worker identity is required before runtime command sync can start.");
        if (PollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException("Runtime command poll interval must be positive.");
        if (ClaimLeaseDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("Runtime command lease duration must be positive.");
        if (ClaimLeaseDuration < TimeSpan.FromSeconds(1) || ClaimLeaseDuration.TotalSeconds > int.MaxValue)
            throw new InvalidOperationException("Runtime command lease duration must be between 1 second and the maximum supported Platform lease.");
        if (HeartbeatInterval <= TimeSpan.Zero || HeartbeatInterval >= ClaimLeaseDuration)
            throw new InvalidOperationException("Runtime command heartbeat interval must be positive and shorter than the command lease duration.");
        if (LeaseSafetyMargin < TimeSpan.Zero || LeaseSafetyMargin >= ClaimLeaseDuration)
            throw new InvalidOperationException("Runtime command lease safety margin must be zero or positive and shorter than the command lease duration.");
        if (RetryBaseDelay <= TimeSpan.Zero)
            throw new InvalidOperationException("Runtime command retry base delay must be positive.");
        if (RetryMaxDelay < RetryBaseDelay)
            throw new InvalidOperationException("Runtime command retry maximum delay must be greater than or equal to the base delay.");
        if (PayloadRequestTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Workflow artifact payload request timeout must be positive.");
        if (MaxRetryAttempts < 0)
            throw new InvalidOperationException("Runtime command retry attempts must be zero or positive.");
        if (string.IsNullOrWhiteSpace(RuntimeFamily))
            throw new InvalidOperationException("Runtime family is required before advertising artifact capabilities.");
        if (SupportedArtifactSchemaVersions is null || !SupportedArtifactSchemaVersions.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new InvalidOperationException("At least one workflow artifact schema version must be supported.");
        if (Capabilities is null || !Capabilities.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new InvalidOperationException("At least one runtime capability must be advertised.");
        if (MaxPayloadBytes <= 0 || MaxPayloadBytes > Array.MaxLength)
            throw new InvalidOperationException("Maximum workflow artifact payload size must be between 1 byte and the maximum supported runtime buffer.");
    }
}
