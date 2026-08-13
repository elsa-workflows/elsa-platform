using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Manifest;

namespace ValenceControl.Deployment.Artifacts;

public sealed record DeploymentArtifactBuildResult(
    bool Succeeded,
    string? ArtifactId,
    string? OutputPath,
    DeploymentArtifactMetadata? Metadata,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics);

public sealed record DeploymentArtifactInspectionResult(
    bool Succeeded,
    string? ArtifactId,
    DeploymentArtifactMetadata? Metadata,
    EnvironmentManifest? Manifest,
    NormalizedManifest? NormalizedManifest,
    IReadOnlyCollection<DeploymentArtifactEntry> Entries,
    IReadOnlyCollection<DeploymentArtifactChecksumVerification> Checksums,
    IReadOnlyCollection<DeploymentDiagnostic> Diagnostics);
