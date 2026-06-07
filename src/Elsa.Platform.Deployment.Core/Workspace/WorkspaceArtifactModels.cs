using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;

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

public enum WorkspaceArtifactLifecycleStatus
{
    Active,
    Archived
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
    Guid ActorAccountId,
    string? EnvelopeVersion = null,
    string? ArtifactTypeId = null,
    string? ArtifactSchemaVersion = null,
    WorkspaceArtifactDigest? ManifestDigest = null,
    ArtifactPayloadReference? PayloadReference = null,
    ArtifactProducer? Producer = null,
    ArtifactDisplayMetadata? DisplayMetadata = null,
    IReadOnlyList<ArtifactCompatibilityHint>? CompatibilityHints = null);

public sealed record WorkspaceArtifactRegistrationResult(WorkspaceArtifact Artifact, bool Created);

public sealed record WorkspaceArtifactDownload(
    string FileName,
    string ContentType,
    Stream Content);

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
    DateTimeOffset UpdatedAt,
    string? EnvelopeVersion = null,
    string? ArtifactTypeId = null,
    string? ArtifactSchemaVersion = null,
    WorkspaceArtifactDigest? ManifestDigest = null,
    ArtifactPayloadReference? PayloadReference = null,
    ArtifactProducer? Producer = null,
    ArtifactDisplayMetadata? DisplayMetadata = null,
    IReadOnlyList<ArtifactCompatibilityHint>? CompatibilityHints = null,
    WorkspaceArtifactLifecycleStatus Status = WorkspaceArtifactLifecycleStatus.Active,
    DateTimeOffset? ArchivedAt = null,
    Guid? ArchivedByAccountId = null)
{
    public ArtifactEnvelope ToEnvelope() =>
        new(
            ArtifactId,
            EnvelopeVersion ?? ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeId ?? ArtifactTypeIds.ElsaWorkflowDefinition,
            ArtifactSchemaVersion ?? ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            new ArtifactDigest(ContentDigest.Algorithm, ContentDigest.Value),
            ManifestDigest is null ? null : new ArtifactDigest(ManifestDigest.Algorithm, ManifestDigest.Value),
            PayloadReference ?? new ArtifactPayloadReference(ReferenceProvider, Reference),
            Producer ?? new ArtifactProducer("manual", "Manual registration"),
            DisplayMetadata ?? new ArtifactDisplayMetadata(Manifest.Name, Manifest.Version, null, new Dictionary<string, string>(), new Dictionary<string, string>(), Manifest.Environment),
            CompatibilityHints ?? [new ArtifactCompatibilityHint(ArtifactTypeIds.ElsaWorkflowDefinition, "elsa-workflows", null, [ArtifactApplyCapability.For(ArtifactTypeIds.ElsaWorkflowDefinition)], new Dictionary<string, string>())],
            Diagnostics.Select(x => new ArtifactEnvelopeDiagnostic(x.Code, x.Severity.ToEnvelopeSeverity(), x.Message)).ToList());
}

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

public static class WorkspaceArtifactEnvelopeExtensions
{
    public static ArtifactEnvelopeDiagnosticSeverity ToEnvelopeSeverity(this WorkspaceArtifactDiagnosticSeverity severity) =>
        severity switch
        {
            WorkspaceArtifactDiagnosticSeverity.Error => ArtifactEnvelopeDiagnosticSeverity.Error,
            WorkspaceArtifactDiagnosticSeverity.Warning => ArtifactEnvelopeDiagnosticSeverity.Warning,
            _ => ArtifactEnvelopeDiagnosticSeverity.Info
        };
}
