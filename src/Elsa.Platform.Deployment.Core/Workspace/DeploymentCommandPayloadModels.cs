namespace Elsa.Platform.Deployment.Core.Workspace;

public enum DeploymentCommandArtifactStatus
{
    Pending,
    Downloading,
    Validated,
    Applying,
    Applied,
    Failed,
    Rejected,
    Skipped
}

public sealed record DeploymentCommandArtifactItem(
    Guid ArtifactRecordId,
    string ArtifactId,
    string ArtifactTypeId,
    string? ArtifactSchemaVersion,
    WorkspaceArtifactDigest ContentDigest,
    string DisplayName,
    string? DownloadUrl,
    DeploymentCommandArtifactStatus Status = DeploymentCommandArtifactStatus.Pending,
    WorkspaceArtifactDigest? ObservedDigest = null,
    string? RuntimeReference = null,
    IReadOnlyList<DeploymentCommandDiagnostic>? Diagnostics = null);

public sealed record DeploymentCommandArtifactOutcome(
    Guid ArtifactRecordId,
    DeploymentCommandArtifactStatus Status,
    WorkspaceArtifactDigest? ObservedDigest = null,
    string? RuntimeReference = null,
    IReadOnlyList<DeploymentCommandDiagnostic>? Diagnostics = null);
