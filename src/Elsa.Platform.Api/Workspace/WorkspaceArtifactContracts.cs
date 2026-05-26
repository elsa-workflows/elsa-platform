using Elsa.Platform.Deployment.Core.Workspace;

namespace Elsa.Platform.Api.Workspace;

public sealed record WorkspaceArtifactListResponse(IReadOnlyList<WorkspaceArtifact> Items);

public sealed record WorkspaceArtifactRegistrationRequest(
    string ArtifactId,
    string LayoutVersion,
    WorkspaceArtifactDigest ContentDigest,
    WorkspaceArtifactFormat Format,
    string ReferenceProvider,
    string Reference,
    WorkspaceArtifactManifestSummary Manifest,
    IReadOnlyList<WorkspaceArtifactResourceSummary> Resources,
    IReadOnlyList<WorkspaceArtifactDiagnostic> Diagnostics);
