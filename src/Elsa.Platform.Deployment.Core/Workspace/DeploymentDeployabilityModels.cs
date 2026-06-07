using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public enum DeploymentDeployabilityStatus
{
    Deployable,
    Warning,
    Blocked
}

public enum DeploymentBlockerScope
{
    Artifact,
    Engine,
    EngineCapabilities,
    Payload,
    Permission,
    Tier,
    Command
}

public sealed record DeploymentDeployabilityRequest(
    Guid TargetEnvironmentId,
    Guid TargetEngineId,
    DeploymentRunMode Mode);

public sealed record DesiredStateArtifactReference(
    string Name,
    Guid? ArtifactRecordId,
    string? ArtifactId,
    string? ArtifactTypeId,
    WorkspaceArtifactDigest? ContentDigest);

public sealed record DeploymentDeployabilityResult(
    Guid WorkspaceId,
    Guid RevisionId,
    Guid EnvironmentId,
    Guid TargetEngineId,
    DeploymentRunMode Mode,
    DeploymentDeployabilityStatus Status,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<ArtifactDeployabilityResult> Artifacts,
    IReadOnlyList<DeploymentBlocker> Blockers)
{
    public bool CanDeploy => Status != DeploymentDeployabilityStatus.Blocked;
}

public sealed record ArtifactDeployabilityResult(
    Guid? ArtifactRecordId,
    string RecordName,
    string? ArtifactId,
    string? ArtifactTypeId,
    string? ArtifactSchemaVersion,
    WorkspaceArtifactDigest? ContentDigest,
    DeploymentDeployabilityStatus Status,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> MissingCapabilities,
    bool PayloadAvailable,
    IReadOnlyList<DeploymentValidation> Diagnostics);

public sealed record ArtifactApplyRequirement(
    string ArtifactTypeId,
    string? ArtifactSchemaVersion,
    string? RuntimeFamily,
    string? RuntimeVersionRange,
    IReadOnlyList<string> RequiredCapabilities,
    string Source);

public sealed record DeploymentBlocker(
    string Id,
    ValidationSeverity Severity,
    DeploymentBlockerScope Scope,
    string Message,
    string Remediation,
    Guid? ArtifactRecordId = null,
    Guid? EngineId = null);
