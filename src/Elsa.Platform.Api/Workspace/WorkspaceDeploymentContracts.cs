using System.Text.Json;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;

namespace Elsa.Platform.Api.Workspace;

public sealed record WorkspaceDeploymentPermissionsResponse(IReadOnlyList<string> Permissions);

public sealed record WorkspaceDeploymentApplicationRequest(string Name, string? Description);

public sealed record WorkspaceDeploymentEnvironmentRequest(string Name, EnvironmentTier? Tier = null, Guid? TierId = null);

public sealed record WorkspaceDeploymentTierCapabilitiesResponse(IReadOnlyList<DeploymentTierCapability> Capabilities);

public sealed record WorkspaceDeploymentTiersResponse(IReadOnlyList<WorkspaceDeploymentTier> Tiers);

public sealed record WorkspaceDeploymentTierRequest(
    string Name,
    string? Description,
    int SortOrder,
    IReadOnlyList<string> Capabilities,
    bool ImpactAccepted = false);

public sealed record WorkspaceDeploymentTierImpactPreviewRequest(IReadOnlyList<string> Capabilities);

public sealed record WorkspaceWorkflowEngineRequest(
    string Name,
    string BaseUrl,
    string? Region,
    string CredentialProvider,
    string CredentialReference,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider);

public sealed record WorkspaceEngineHeartbeatRequest(
    Guid EnvironmentId,
    string? Version,
    CertificateStatus CertificateStatus,
    CredentialVerificationStatus CredentialVerificationStatus,
    DateTimeOffset HeartbeatAt,
    IReadOnlyList<EngineCapability>? Capabilities,
    string? Message);

public sealed record WorkspaceDesiredStateRevisionRequest(
    string Label,
    string? Commit,
    IReadOnlyList<WorkspaceDesiredStateRecordRequest> Records);

public sealed record WorkspaceDesiredStateRecordRequest(
    DesiredStateRecordKind Kind,
    string Name,
    JsonElement Payload);

public sealed record WorkspacePromotionPreviewRequestDto(
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    Guid SourceRevisionId,
    Guid TargetEngineId);

public sealed record WorkspaceActionConfirmationRequest(
    ConfirmationActionType ActionType,
    string TargetId,
    int? LifetimeSeconds);

public sealed record WorkspaceDeploymentRunRequestDto(
    Guid SourceRevisionId,
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
    Guid ConfirmationId,
    DeploymentRunMode Mode);

public sealed record WorkspaceRollbackRunRequestDto(
    Guid SourceRevisionId,
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
    Guid ConfirmationId,
    Guid RollbackSourceRunId,
    DeploymentRunMode Mode);

public sealed record WorkspaceDeploymentRunDetailResponse(
    WorkspaceDeploymentRun Run,
    IReadOnlyList<DeploymentRunHistoryEvent> History);

public sealed record WorkspaceRuntimeControlRunRequest(Guid ConfirmationId);
