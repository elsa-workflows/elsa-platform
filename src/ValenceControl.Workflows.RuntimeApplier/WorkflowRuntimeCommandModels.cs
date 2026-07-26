using ValenceControl.Deployment.Abstractions.Artifacts;

namespace ValenceControl.Workflows.RuntimeApplier;

public enum WorkflowRuntimeCommandAction
{
    Unknown = 0,
    Deploy = 1,
    Rollback = 2,
    Validate = 3,
    RuntimeControl = 4
}

public enum WorkflowRuntimeCommandStatus
{
    Unknown = 0,
    Pending = 1,
    Claimed = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Rejected = 6,
    Cancelled = 7,
    RecoveryRequired = 8,
    Expired = 9
}

public enum WorkflowRuntimeCommandClientStatus
{
    Unknown = 0,
    Succeeded = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    RetryableError = 5,
    ValidationFailed = 6
}

public sealed record WorkflowRuntimeCommandArtifactReference(
    Guid? ArtifactRecordId,
    string? ArtifactId,
    string? ArtifactTypeId,
    ArtifactDigest? ContentDigest);

public sealed record WorkflowRuntimeCommandRevisionReference(Guid? RevisionId);

public sealed record WorkflowRuntimeCommand(
    Guid Id,
    Guid WorkspaceId,
    Guid RunId,
    Guid EnvironmentId,
    Guid EngineId,
    WorkflowRuntimeCommandAction Action,
    WorkflowRuntimeCommandStatus Status,
    WorkflowRuntimeCommandArtifactReference? Artifact,
    WorkflowRuntimeCommandRevisionReference? Revision,
    string IdempotencyKey,
    string? WorkerId,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? HeartbeatAt,
    int AttemptNumber,
    int? PercentComplete,
    string? ProgressMessage,
    ArtifactDigest? ObservedArtifactDigest,
    string? RuntimeReference,
    IReadOnlyList<WorkflowArtifactDiagnostic> Diagnostics,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AvailableAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CompletedAt);

public sealed record WorkflowRuntimeCommandClaim(
    WorkflowRuntimeCommand Command,
    string LeaseToken);

public sealed record WorkflowRuntimeCommandClaimResult(
    WorkflowRuntimeCommandClientStatus Status,
    string Message,
    WorkflowRuntimeCommandClaim? Claim = null)
{
    public bool Succeeded => Status == WorkflowRuntimeCommandClientStatus.Succeeded;
}

public sealed record WorkflowRuntimeCommandReportResult(
    WorkflowRuntimeCommandClientStatus Status,
    string Message,
    WorkflowRuntimeCommand? Command = null)
{
    public bool Succeeded => Status == WorkflowRuntimeCommandClientStatus.Succeeded;
}
