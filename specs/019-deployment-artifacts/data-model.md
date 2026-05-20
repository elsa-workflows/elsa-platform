# Data Model: Deployment Artifact Packaging

## DeploymentArtifactBuildOptions

Represents one artifact build request.

- `Manifest`: Parsed `EnvironmentManifest`.
- `WorkspaceRoot`: Root directory from which relative manifest payload paths are resolved.
- `OutputPath`: Destination folder or ZIP path.
- `Format`: `Folder` or `Zip`.
- `ManifestFileName`: Optional manifest snapshot name; defaults to `manifest.yaml` or `manifest.json` based on caller input.
- `Overwrite`: Whether an existing output path may be replaced.

Validation:

- Workspace root must exist.
- Output path must not be inside a referenced payload path.
- Manifest must parse and normalize without errors before build succeeds.
- Build format must be supported.

## DeploymentArtifactBuildResult

Represents the outcome of an artifact build.

- `Succeeded`: True when artifact output is complete and valid.
- `ArtifactId`: Content-derived artifact identity when build succeeds.
- `OutputPath`: Published artifact location when build succeeds.
- `Metadata`: Artifact metadata when build succeeds.
- `Diagnostics`: Structured diagnostics for all failures or warnings.

State rules:

- If any error diagnostic exists, `Succeeded` is false.
- Failed builds must not report partial output as a valid artifact.

## DeploymentArtifactInspectionResult

Represents a read-only artifact inspection.

- `Succeeded`: True when layout, metadata, manifest, payload references, and checksums are valid.
- `ArtifactId`: Artifact identity from verified metadata.
- `Metadata`: Parsed artifact metadata.
- `Manifest`: Parsed manifest snapshot.
- `Resources`: Normalized manifest resources.
- `Entries`: Logical artifact entries.
- `Checksums`: Checksum inventory and verification status.
- `Diagnostics`: Structured diagnostics.

State rules:

- Missing or changed files make `Succeeded` false.
- Unsupported layout versions make `Succeeded` false.
- Empty but well-formed artifacts are readable and invalid for apply-oriented consumers.

## DeploymentArtifactMetadata

Versioned descriptor stored inside every artifact.

- `LayoutVersion`: Must be `platform.elsa.io/deployment-artifact/v1alpha1`.
- `ArtifactId`: Content-derived identity.
- `CreatedAt`: Informational timestamp excluded from content identity.
- `Manifest`: Manifest name, version, environment, labels, annotations.
- `ResourceSummary`: Resource counts and resource identifiers.
- `ContentDigest`: SHA-256 digest over canonical artifact content.

Validation:

- Layout version is required and must be supported.
- Artifact ID and content digest must match computed values.
- Metadata must not contain raw secrets.

## DeploymentArtifactEntry

Represents a logical artifact item.

- `Path`: Normalized artifact-relative path using `/`.
- `Kind`: `Metadata`, `Manifest`, `ChecksumInventory`, or `Payload`.
- `SourcePath`: Optional workspace-relative source path for payload entries.
- `Size`: Content length in bytes.

Validation:

- Paths must be relative, normalized, and traversal-free.
- Duplicate normalized paths are rejected.

## DeploymentArtifactChecksumEntry

Represents checksum data for one artifact entry.

- `Path`: Artifact-relative path.
- `Kind`: Entry kind.
- `Algorithm`: `sha256` in Phase 1.
- `Digest`: Lowercase hex digest.
- `Size`: Content length in bytes.

Validation:

- Algorithm must be supported.
- Digest must match entry bytes.
- Checksum inventory must not include unexpected paths.

## DeploymentArtifactDiagnostic

Uses `DeploymentDiagnostic` from deployment abstractions.

Initial diagnostic codes:

- `artifact.layout.unsupported`
- `artifact.metadata.required`
- `artifact.manifest.required`
- `artifact.path.invalid`
- `artifact.path.duplicate`
- `artifact.payload.missing`
- `artifact.payload.unexpected`
- `artifact.checksum.missing`
- `artifact.checksum.mismatch`
- `artifact.archive.invalid`
- `artifact.build.failed`
