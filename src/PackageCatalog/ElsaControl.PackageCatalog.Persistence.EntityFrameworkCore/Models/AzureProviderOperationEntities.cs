using ElsaControl.Deployment.Azure;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;

internal sealed class AzureProviderOperationEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string TargetKey { get; set; } = "";
    public AzureProviderOperationAction Action { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string OperationIdentity { get; set; } = "";
    public string PlanFingerprint { get; set; } = "";
    public string TemplateFingerprint { get; set; } = "";
    public string ElsaVersion { get; set; } = "";
    public string ReleaseLine { get; set; } = "";
    public string Topology { get; set; } = "";
    public string Isolation { get; set; } = "";
    public string Location { get; set; } = "";
    public string ImageRepository { get; set; } = "";
    public string ImageDigest { get; set; } = "";
    public string? ReleaseManifestDigest { get; set; }
    public string? ReleaseManifestSignatureDigest { get; set; }
    public AzureProviderOperationStatus Status { get; set; }
    public AzureProviderOperationPhase Phase { get; set; }
    public long CheckpointSequence { get; set; }
    public long Version { get; set; }
    public string? ResourceGroupName { get; set; }
    public string? FoundationDeploymentId { get; set; }
    public string? WorkloadDeploymentId { get; set; }
    public string? WorkloadResourceId { get; set; }
    public string? WorkloadRevisionName { get; set; }
    public string? StableTrafficRevisionName { get; set; }
    public string? Endpoint { get; set; }
    public AzureProviderHealth Health { get; set; }
    public string DiagnosticsJson { get; set; } = "[]";
    public string? WorkerId { get; set; }
    public string? LeaseTokenHash { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<AzureProviderOperationTransitionEntity> Transitions { get; set; } = [];
}

internal sealed class AzureProviderOperationTransitionEntity
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public AzureProviderOperationEntity? Operation { get; set; }
    public long Sequence { get; set; }
    public AzureProviderOperationStatus Status { get; set; }
    public AzureProviderOperationPhase Phase { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
