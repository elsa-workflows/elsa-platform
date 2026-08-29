namespace ElsaControl.Deployment.Azure;

public enum AzureProviderOperationAction
{
    Reconcile,
    Delete
}

public enum AzureProviderOperationStatus
{
    Accepted,
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    RecoveryRequired
}

public enum AzureProviderOperationPhase
{
    Planned,
    FoundationSubmitted,
    FoundationReady,
    WorkloadSubmitted,
    WorkloadReady,
    HealthVerified,
    TrafficPromoted,
    CleanupSubmitted,
    CleanupVerified
}

public enum AzureProviderHealth
{
    Unknown,
    Healthy,
    Degraded,
    Unreachable,
    Failed
}

public sealed record AzureProviderOperationRequest(
    Guid WorkspaceId,
    string TargetKey,
    AzureProviderOperationAction Action,
    string IdempotencyKey,
    string PlanFingerprint,
    string TemplateFingerprint,
    string ElsaVersion,
    string ReleaseLine,
    string Topology,
    string Isolation,
    string Location,
    string ImageRepository,
    string ImageDigest,
    string? ReleaseManifestDigest = null,
    string? ReleaseManifestSignatureDigest = null);

public sealed record AzureProviderResourceReferences(
    string? ResourceGroupName = null,
    string? FoundationDeploymentId = null,
    string? WorkloadDeploymentId = null,
    string? WorkloadResourceId = null,
    string? WorkloadRevisionName = null,
    string? StableTrafficRevisionName = null);

public sealed record AzureProviderDiagnostic(string Code, string Message);

public sealed record AzureProviderOperation(
    Guid Id,
    Guid WorkspaceId,
    string TargetKey,
    AzureProviderOperationAction Action,
    string IdempotencyKey,
    string RequestHash,
    string OperationIdentity,
    string PlanFingerprint,
    string TemplateFingerprint,
    string ElsaVersion,
    string ReleaseLine,
    string Topology,
    string Isolation,
    string Location,
    string ImageRepository,
    string ImageDigest,
    string? ReleaseManifestDigest,
    string? ReleaseManifestSignatureDigest,
    AzureProviderOperationStatus Status,
    AzureProviderOperationPhase Phase,
    long CheckpointSequence,
    long Version,
    AzureProviderResourceReferences Resources,
    string? Endpoint,
    AzureProviderHealth Health,
    IReadOnlyList<AzureProviderDiagnostic> Diagnostics,
    string? WorkerId,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? HeartbeatAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record AzureProviderOperationTransition(
    Guid Id,
    Guid OperationId,
    long Sequence,
    AzureProviderOperationStatus Status,
    AzureProviderOperationPhase Phase,
    string Code,
    string Message,
    DateTimeOffset OccurredAt);

public sealed record AzureProviderCheckpoint(
    AzureProviderOperationPhase Phase,
    string Code,
    string Message,
    AzureProviderResourceReferences Resources,
    string? Endpoint,
    AzureProviderHealth Health,
    IReadOnlyList<AzureProviderDiagnostic> Diagnostics);
