using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Api.Workspace;

public sealed record WorkspaceArtifactListResponse(IReadOnlyList<WorkspaceArtifact> Items);

public sealed record WorkspaceArtifactTypeListResponse(IReadOnlyList<ArtifactTypeDefinition> Items);

public sealed record WorkspaceArtifactRegistrationRequest(
    string ArtifactId,
    string LayoutVersion,
    WorkspaceArtifactDigest ContentDigest,
    WorkspaceArtifactFormat Format,
    string ReferenceProvider,
    string Reference,
    WorkspaceArtifactManifestSummary Manifest,
    IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics,
    string? EnvelopeVersion = null,
    string? ArtifactTypeId = null,
    string? ArtifactSchemaVersion = null,
    WorkspaceArtifactDigest? ManifestDigest = null,
    ArtifactPayloadReference? PayloadReference = null,
    ArtifactProducer? Producer = null,
    ArtifactDisplayMetadata? DisplayMetadata = null,
    IReadOnlyList<ArtifactCompatibilityHint>? CompatibilityHints = null);

public sealed record WorkspaceArtifactUploadCapabilitiesResponse(
    long MaxUploadBytes,
    bool SampleArtifactGenerationEnabled);
