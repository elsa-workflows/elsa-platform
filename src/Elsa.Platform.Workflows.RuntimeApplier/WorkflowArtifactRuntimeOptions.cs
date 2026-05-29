using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed record WorkflowArtifactRuntimeOptions
{
    public Uri? PlatformEndpoint { get; init; }

    public Guid? WorkspaceId { get; init; }

    public Guid? EngineId { get; init; }

    public string WorkerId { get; init; } = $"{Environment.MachineName}:{Environment.ProcessId}";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ClaimLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public string RuntimeFamily { get; init; } = "elsa-workflows";

    public string? RuntimeVersion { get; init; }

    public IReadOnlyList<string> SupportedArtifactSchemaVersions { get; init; } =
        [ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion];

    public IReadOnlyList<string> Capabilities { get; init; } = ["workflow-definition.apply"];

    public long MaxPayloadBytes { get; init; } = 4 * 1024 * 1024;

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
        if (string.IsNullOrWhiteSpace(RuntimeFamily))
            throw new InvalidOperationException("Runtime family is required before advertising artifact capabilities.");
        if (SupportedArtifactSchemaVersions is null || !SupportedArtifactSchemaVersions.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new InvalidOperationException("At least one workflow artifact schema version must be supported.");
        if (Capabilities is null || !Capabilities.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new InvalidOperationException("At least one runtime capability must be advertised.");
        if (MaxPayloadBytes <= 0)
            throw new InvalidOperationException("Maximum workflow artifact payload size must be positive.");
    }
}
