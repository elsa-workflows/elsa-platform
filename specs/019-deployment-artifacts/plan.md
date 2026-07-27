# Implementation Plan: Deployment Artifact Packaging

**Branch**: `019-deployment-artifacts` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/019-deployment-artifacts/spec.md`

## Summary

Add `ValenceControl.Deployment.Artifacts` as the next Phase 1 deployment package. The package builds immutable folder artifacts from manifest-normalized resources and workspace payload files, writes the same logical artifact as ZIP, reads folder/ZIP artifacts, verifies SHA-256 checksums, and returns structured diagnostics for invalid layout, missing files, path traversal, unsupported versions, and checksum drift. This slice stops before deployment planning, validation orchestration, dry-run, apply, history, CLI, API, OCI, NuGet, signing, policy, and operator/GitOps integration.

## Technical Context

**Language/Version**: C# on .NET 10 using repository-wide `Directory.Build.props`.

**Primary Dependencies**: `ValenceControl.Deployment.Abstractions`, `ValenceControl.Deployment.Manifest`, `System.Text.Json`, `System.IO.Compression`.

**Storage**: Local folder and ZIP artifact IO only. No database, object storage, registry, or remote transport.

**Testing**: xUnit and its built-in assertions in `tests/ValenceControl.Deployment.Artifacts.Tests`, plus full solution `dotnet test`.

**Target platform**: Cross-platform .NET library consumed by future engine, CLI, API, GitOps, and operator slices.

**Project Type**: Multi-project .NET repository; this slice adds one source library and one test project.

**Performance Goals**: Deterministic artifact identity and checksum verification for normal CI/CD artifact sizes. Streaming is preferred for payload copying and checksum computation, but no large-artifact throughput target is required in Phase 1.

**Constraints**: The artifact package must not depend on deployment engine, CLI, API, hosting, persistence, Package Catalog implementation, Runtime Builder implementation, Kubernetes, OCI, signing, policy, or runtime-state packages. Artifacts must not contain raw secrets.

**Scale/Scope**: Phase 1 folder and ZIP artifacts for one deployment manifest and its manifest-declared payload files.

## Constitution Check

- **Control Plane First**: Pass. Artifacts package control-plane desired state only and exclude runtime execution state.
- **Bounded Subsystems**: Pass. Artifacts depend only on deployment abstractions, manifest contracts, and serialization/archive primitives.
- **Contract Stability**: Pass. Layout version is explicit: `valence-control/deployment-artifact/v1alpha1`.
- **Safety By Design**: Pass. Path traversal is rejected and raw secrets are out of scope.
- **Incremental Verifiability**: Pass. Build, read, inspect, verify, and boundary tests are independently executable before engine work begins.

## Project Structure

### Documentation (this feature)

```text
specs/019-deployment-artifacts/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── artifact-layout.md
│   └── dependency-boundaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ValenceControl.Deployment.Artifacts/
    ArtifactDiagnosticCodes.cs
    ArtifactLayoutConstants.cs
    ArtifactBuildOptions.cs
    ArtifactBuildResult.cs
    ArtifactInspectionResult.cs
    ArtifactMetadata.cs
    ArtifactEntry.cs
    ArtifactChecksumEntry.cs
    IDeploymentArtifactBuilder.cs
    IDeploymentArtifactReader.cs
    DeploymentArtifactBuilder.cs
    DeploymentArtifactReader.cs
    DeploymentArtifactPathValidator.cs
    DeploymentArtifactChecksumService.cs

tests/
  ValenceControl.Deployment.Artifacts.Tests/
    ArtifactBuilderTests.cs
    ArtifactReaderTests.cs
    ArtifactChecksumTests.cs
    ArtifactPathValidationTests.cs
    ArtifactBoundaryTests.cs
```

**Structure Decision**: Keep artifact IO in `ValenceControl.Deployment.Artifacts`. Manifest parsing/normalization remains in `ValenceControl.Deployment.Manifest`; reconciliation remains in `ValenceControl.Deployment.Engine`.

## Phase Plan

### Phase 1: Planning And Contracts

Outcome:

- Spec Kit artifacts define artifact layout, metadata, checksum, boundary, and verification contracts.

Exit gate:

- `/speckit-analyze` finds no critical inconsistencies.

### Phase 2: Project Skeleton

Outcome:

- Source/test projects exist, are added to `ValenceControl.sln`, and reference only allowed packages.

Exit gate:

- Empty artifact project builds and boundary tests can compile.

### Phase 3: Folder Artifact Build

Outcome:

- Builder writes atomic folder artifacts with metadata, manifest snapshot, payload files, and checksum inventory.
- Path traversal, missing files, duplicate artifact paths, unsupported manifest states, and partial output are handled with diagnostics.

Exit gate:

- Folder build tests pass and deterministic identity is verified.

### Phase 4: Artifact Read, Inspect, And Verify

Outcome:

- Reader inspects folder artifacts and verifies SHA-256 checksums.
- Diagnostics distinguish missing, changed, unexpected, malformed, and unsupported layout inputs.

Exit gate:

- Inspection and checksum drift tests pass without any deployment target or engine dependency.

### Phase 5: ZIP Artifact Parity

Outcome:

- ZIP writer/reader produces the same logical inspection results as folder artifacts.
- Archive path traversal entries are rejected.

Exit gate:

- Folder/ZIP parity and archive safety tests pass.

### Phase 6: Boundaries And Verification

Outcome:

- Dependency-boundary tests enforce allowed references.
- Quickstart and contracts match implementation behavior.

Exit gate:

- Focused artifact tests, full solution tests, and `git diff --check` pass.

## Deferred Work

- Deployment planning, validation orchestration, diff, dry-run, apply, and history persistence.
- CLI and API commands/endpoints.
- OCI artifacts, NuGet artifact packaging, signatures, attestations, policy evaluation, approvals, overlays, secret resolution, GitOps, operators, Kubernetes CRDs, and fleet reconciliation.
- Runtime-specific resource handlers and package catalog compatibility checks.

## Complexity Tracking

No constitution violations are introduced.
