using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Models;

internal sealed class DeploymentApplicationEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public List<DeploymentEnvironmentEntity> Environments { get; set; } = [];
}

internal sealed class DeploymentEnvironmentEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public DeploymentApplicationEntity? Application { get; set; }
    public string Name { get; set; } = "";
    public EnvironmentTier Tier { get; set; }
    public Guid? DesiredRevisionId { get; set; }
    public Guid? DeployedRevisionId { get; set; }
    public DeploymentStatus DeploymentStatus { get; set; }
    public DriftStatus DriftStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<WorkflowEngineEntity> Engines { get; set; } = [];
    public List<DesiredStateRevisionEntity> Revisions { get; set; } = [];
}

internal sealed class WorkflowEngineEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? Region { get; set; }
    public string? Version { get; set; }
    public CertificateStatus CertificateStatus { get; set; }
    public string CredentialProvider { get; set; } = "";
    public string CredentialReference { get; set; } = "";
    public CredentialVerificationStatus CredentialVerificationStatus { get; set; }
    public DateTimeOffset? CredentialLastVerifiedAt { get; set; }
    public DeploymentHealth Health { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string? HostingProvider { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<EngineCapabilityEntity> Capabilities { get; set; } = [];
    public List<RuntimeControlEntity> Controls { get; set; } = [];
}

internal sealed class EngineCapabilityEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EngineId { get; set; }
    public WorkflowEngineEntity? Engine { get; set; }
    public string CapabilityId { get; set; } = "";
    public string Label { get; set; } = "";
    public CapabilityBoundary Boundary { get; set; }
}

internal sealed class RuntimeControlEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid EngineId { get; set; }
    public WorkflowEngineEntity? Engine { get; set; }
    public string ControlId { get; set; } = "";
    public string Label { get; set; } = "";
    public CapabilityBoundary Boundary { get; set; }
    public string RequiredCapabilityId { get; set; } = "";
    public string Description { get; set; } = "";
}

internal sealed class DesiredStateRevisionEntity
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DeploymentEnvironmentEntity? Environment { get; set; }
    public int RevisionNumber { get; set; }
    public string Label { get; set; } = "";
    public string? Commit { get; set; }
    public string ContentHash { get; set; } = "";
    public string DesiredStateJson { get; set; } = "";
    public DateTimeOffset AuthoredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByAccountId { get; set; }
}
