using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public interface IWorkflowArtifactPayloadFetcher
{
    Task<WorkflowArtifactPayload> FetchAsync(ArtifactPayloadReference reference, CancellationToken cancellationToken = default);
}

public interface IWorkflowArtifactSchemaValidator
{
    Task<WorkflowArtifactValidationResult> ValidateAsync(
        ArtifactEnvelope envelope,
        WorkflowArtifactPayload payload,
        WorkflowArtifactRuntimeCapability capability,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowDefinitionApplier
{
    Task<WorkflowArtifactApplyResult> ApplyAsync(
        WorkflowArtifactApplyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkflowArtifactPayload(
    ArtifactPayloadReference Reference,
    byte[] Content,
    string? MediaType = null)
{
    public long SizeBytes => Content.LongLength;
}

public sealed record WorkflowArtifactApplyRequest(
    Guid CommandId,
    string IdempotencyKey,
    ArtifactEnvelope Envelope,
    string WorkflowDefinitionJson,
    ArtifactDigest ObservedDigest);

public sealed record WorkflowArtifactApplyResult(
    WorkflowArtifactApplyStatus Status,
    ArtifactDigest? ObservedDigest,
    string? RuntimeReference,
    IReadOnlyList<WorkflowArtifactDiagnostic> Diagnostics)
{
    public bool Succeeded => Status is WorkflowArtifactApplyStatus.Applied or WorkflowArtifactApplyStatus.AlreadyApplied;
}

public enum WorkflowArtifactApplyStatus
{
    Unknown = 0,
    Applied = 1,
    AlreadyApplied = 2,
    Rejected = 3,
    Failed = 4
}

public sealed record WorkflowArtifactValidationResult(
    WorkflowArtifactValidationStatus Status,
    ArtifactDigest? ObservedDigest,
    IReadOnlyList<WorkflowArtifactDiagnostic> Diagnostics)
{
    public bool Succeeded => Status == WorkflowArtifactValidationStatus.Valid;

    public static WorkflowArtifactValidationResult Valid(ArtifactDigest observedDigest) =>
        new(WorkflowArtifactValidationStatus.Valid, observedDigest, []);

    public static WorkflowArtifactValidationResult Invalid(
        WorkflowArtifactValidationStatus status,
        WorkflowArtifactDiagnostic diagnostic,
        ArtifactDigest? observedDigest = null) =>
        new(status, observedDigest, [diagnostic]);
}

public enum WorkflowArtifactValidationStatus
{
    Unknown = 0,
    Valid = 1,
    UnsupportedArtifactType = 2,
    UnsupportedSchema = 3,
    MissingCapability = 4,
    PayloadTooLarge = 5,
    DigestMismatch = 6,
    InvalidPayload = 7
}

public sealed record WorkflowArtifactDiagnostic(
    string Code,
    WorkflowArtifactDiagnosticSeverity Severity,
    string Message);

public enum WorkflowArtifactDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
