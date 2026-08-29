using System.Text.Json;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Api.Workspace;

public sealed record WorkspaceDeploymentPermissionsResponse(IReadOnlyList<string> Permissions);

public sealed record WorkspaceDeploymentApplicationRequest(string Name, string? Description);

public sealed record WorkspaceDeploymentEnvironmentRequest(string Name, EnvironmentTier? Tier = null, Guid? TierId = null);

public sealed record WorkspaceDeploymentTierCapabilitiesResponse(IReadOnlyList<DeploymentTierCapability> Capabilities);

public sealed record WorkspaceDeploymentTiersResponse(IReadOnlyList<WorkspaceDeploymentTier> Tiers);

public sealed record WorkspaceDesiredStateRequirementsResponse(
    Guid EnvironmentId,
    string EnvironmentName,
    string TierName,
    IReadOnlyList<string> TierCapabilities,
    IReadOnlyList<DesiredStateRequirement> Requirements);

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
    string? CredentialProvider,
    string? CredentialReference,
    IReadOnlyList<EngineCapability> Capabilities,
    IReadOnlyList<RuntimeControl> Controls,
    string? HostingProvider,
    Guid? CredentialReferenceId = null,
    EngineCredentialAssignmentStatus CredentialAssignmentStatus = EngineCredentialAssignmentStatus.Assigned);

public sealed record WorkspaceDeploymentSecretStoresResponse(IReadOnlyList<WorkspaceDeploymentSecretStore> Items);

public sealed record WorkspaceDeploymentSecretStoreRequest(
    string Name,
    string? Provider,
    string? Description,
    DeploymentSecretStoreType Type = DeploymentSecretStoreType.GenericExternalReference);

public sealed record WorkspaceDeploymentCredentialReferencesResponse(IReadOnlyList<WorkspaceDeploymentCredentialReference> Items);

public sealed record WorkspaceDeploymentCredentialReferenceRequest(
    string Name,
    string Reference,
    string? Description,
    string? SecretValue = null);

public sealed record WorkspaceDeploymentCredentialReferenceRotateRequest(string SecretValue);

public sealed record WorkspaceDeploymentCredentialReferenceUsageResponse(IReadOnlyList<WorkspaceDeploymentCredentialUsage> Items);

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

public sealed record WorkspaceApplicationRevisionsResponse(IReadOnlyList<WorkspaceDesiredStateRevisionSummary> Items);

public sealed record WorkspaceDesiredStateRecordRequest(
    DesiredStateRecordKind Kind,
    string Name,
    JsonElement Payload);

public sealed record WorkspacePromotionPreviewRequestDto(
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    Guid SourceRevisionId,
    Guid TargetEngineId);

public sealed record WorkspacePromotionRequestDto(
    Guid SourceEnvironmentId,
    Guid TargetEnvironmentId,
    Guid SourceRevisionId,
    Guid TargetEngineId,
    string Label,
    string? Commit);

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

public sealed record WorkspaceDeployabilityRequestDto(
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
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
    IReadOnlyList<DeploymentRunHistoryEvent> History,
    IReadOnlyList<DeploymentRunCommandSummary> Commands);

public sealed record WorkspaceRuntimeControlRunRequest(Guid ConfirmationId);

/// <summary>
/// API shape for a previously admitted Azure provider plan. The endpoint accepts only the
/// provider-safe projection; admission of the provider-neutral resolved plan happens upstream.
/// </summary>
public sealed record AzureProviderOperationSubmissionRequest(
    string IdempotencyKey,
    string TemplateFingerprint,
    string PlanFingerprint,
    string WorkloadName,
    string Location,
    string ElsaVersion,
    string ReleaseLine,
    string Topology,
    string Isolation,
    string ImageRepository,
    string ImageDigest,
    string ReleaseManifestReference,
    string ReleaseManifestDigest,
    string ReleaseManifestSignatureReference,
    string ReleaseManifestSignatureDigest,
    IReadOnlyDictionary<string, string>? SecretReferences = null);

public sealed record AzureProviderOperationResponse(
    AzureProviderOperation Operation,
    IReadOnlyList<AzureProviderOperationTransition> Transitions);
