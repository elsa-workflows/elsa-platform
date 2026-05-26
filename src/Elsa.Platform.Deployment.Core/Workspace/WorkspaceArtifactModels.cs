namespace Elsa.Platform.Deployment.Core.Workspace;

public enum WorkspaceArtifactFormat
{
    Folder,
    Zip,
    Unknown
}

public enum WorkspaceArtifactChecksumStatus
{
    Unverified,
    Verified,
    Missing,
    Mismatched,
    Unexpected,
    Unavailable
}

public enum WorkspaceArtifactInspectionStatus
{
    NeverInspected,
    Valid,
    Invalid,
    Unavailable,
    Unsupported
}

public enum WorkspaceArtifactDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record WorkspaceArtifactDigest(string Algorithm, string Value);

public sealed record WorkspaceArtifactManifestSummary(
    string? Name,
    string? Version,
    string? Environment);

public sealed record WorkspaceArtifactResourceSummary(
    string Type,
    string LogicalId,
    string? Scope,
    string? Version,
    WorkspaceArtifactDigest? DesiredStateHash);

public sealed record WorkspaceArtifactDiagnostic(
    string Code,
    WorkspaceArtifactDiagnosticSeverity Severity,
    string Message);

public sealed record RegisterWorkspaceArtifactRequest(
    string ArtifactId,
    string LayoutVersion,
    WorkspaceArtifactDigest ContentDigest,
    WorkspaceArtifactFormat Format,
    string ReferenceProvider,
    string Reference,
    WorkspaceArtifactManifestSummary Manifest,
    IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics,
    Guid ActorAccountId);

public sealed record WorkspaceArtifactRegistrationResult(WorkspaceArtifact Artifact, bool Created);

public sealed record WorkspaceArtifact(
    Guid Id,
    Guid WorkspaceId,
    string ArtifactId,
    string LayoutVersion,
    WorkspaceArtifactDigest ContentDigest,
    WorkspaceArtifactFormat Format,
    string ReferenceProvider,
    string Reference,
    WorkspaceArtifactManifestSummary Manifest,
    IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
    WorkspaceArtifactChecksumStatus ChecksumStatus,
    WorkspaceArtifactInspectionStatus InspectionStatus,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics,
    DateTimeOffset RegisteredAt,
    Guid RegisteredByAccountId,
    DateTimeOffset? LastInspectedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceArtifactInspectionResult(
    Guid ArtifactRecordId,
    string ArtifactId,
    WorkspaceArtifactChecksumStatus ChecksumStatus,
    WorkspaceArtifactInspectionStatus InspectionStatus,
    DateTimeOffset? LastInspectedAt,
    int ResourceCount,
    IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics);

public sealed record WorkspaceArtifactInspectionUpdate(
    Guid ArtifactRecordId,
    string ArtifactId,
    WorkspaceArtifactChecksumStatus ChecksumStatus,
    WorkspaceArtifactInspectionStatus InspectionStatus,
    DateTimeOffset LastInspectedAt,
    IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics);
