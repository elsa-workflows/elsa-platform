using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed record WorkspaceDeploymentApplication(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CreatedByAccountId,
    Guid? UpdatedByAccountId);

public sealed record WorkspaceDeploymentEnvironment(
    Guid Id,
    Guid WorkspaceId,
    Guid ApplicationId,
    string Name,
    EnvironmentTier Tier,
    Guid? DesiredRevisionId,
    Guid? DeployedRevisionId,
    DeploymentStatus DeploymentStatus,
    DriftStatus DriftStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceWorkflowEngine(
    Guid Id,
    Guid WorkspaceId,
    Guid EnvironmentId,
    string Name,
    string BaseUrl,
    string? Region,
    string? Version,
    CertificateStatus CertificateStatus,
    string CredentialProvider,
    string CredentialReference,
    CredentialVerificationStatus CredentialVerificationStatus,
    DateTimeOffset? CredentialLastVerifiedAt,
    DeploymentHealth Health,
    DateTimeOffset? LastHeartbeatAt,
    string? HostingProvider,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceEngineCapability(
    Guid Id,
    Guid WorkspaceId,
    Guid EngineId,
    string CapabilityId,
    string Label,
    CapabilityBoundary Boundary);

public sealed record WorkspaceRuntimeControlDefinition(
    Guid Id,
    Guid WorkspaceId,
    Guid EngineId,
    string ControlId,
    string Label,
    CapabilityBoundary Boundary,
    string RequiredCapabilityId,
    string Description);

public sealed record WorkspaceDesiredStateRevision(
    Guid Id,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId,
    int RevisionNumber,
    string Label,
    string? Commit,
    string ContentHash,
    string DesiredStateJson,
    DateTimeOffset AuthoredAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByAccountId);

public sealed record WorkspacePromotionPreviewRequest(
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    Guid SourceRevisionId,
    Guid TargetEngineId);

public sealed record CreateWorkflowApplicationRequest(string Name, string? Description, Guid? ActorAccountId);

public sealed record UpdateWorkflowApplicationRequest(string Name, string? Description, Guid? ActorAccountId);

public sealed record CreateDeploymentEnvironmentRequest(Guid ApplicationId, string Name, EnvironmentTier Tier);

public sealed record UpdateDeploymentEnvironmentRequest(Guid ApplicationId, string Name, EnvironmentTier Tier);

public sealed record RegisterWorkflowEngineRequest(
    Guid EnvironmentId,
    string Name,
    string BaseUrl,
    string? Region,
    string CredentialProvider,
    string CredentialReference,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider);

public sealed record UpdateWorkflowEngineRequest(
    string Name,
    string BaseUrl,
    string? Region,
    string CredentialProvider,
    string CredentialReference,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider);

public sealed record CreateDesiredStateRevisionRequest(
    Guid ApplicationId,
    Guid EnvironmentId,
    string Label,
    string? Commit,
    string DesiredStateJson,
    Guid? ActorAccountId);

public sealed record WorkspaceDeploymentRunRequest(
    Guid SourceRevisionId,
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
    Guid ActorAccountId,
    DeploymentRunMode Mode);

public enum DeploymentRunMode
{
    DryRun,
    Apply
}
