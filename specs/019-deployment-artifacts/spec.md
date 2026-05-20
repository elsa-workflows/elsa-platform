# Feature Specification: Deployment Artifact Packaging

**Feature Branch**: `019-deployment-artifacts`

**Created**: 2026-05-20

**Status**: Draft

**Input**: User description: "Implement the Phase 1 deployment artifact package. It should build and read immutable deployment artifacts from a manifest-normalized resource set, supporting folder artifacts and ZIP artifacts, artifact metadata, content checksums, artifact inspection, and artifact diagnostics. It must consume Elsa.Platform.Deployment.Manifest and Elsa.Platform.Deployment.Abstractions, stay transport-agnostic and hosting-agnostic, avoid OCI/NuGet/signatures/policy/engine/apply concerns, and produce contracts that future engine, CLI, API, GitOps, and operator slices can reuse."

## Clarifications

### Session 2026-05-20

- Q: Which checksum algorithm should Phase 1 standardize on? -> A: SHA-256 only for Phase 1; the model keeps an algorithm field for future expansion.
- Q: How should the artifact layout be versioned? -> A: Use an explicit `platform.elsa.io/deployment-artifact/v1alpha1` layout version in artifact metadata.
- Q: What happens when artifact build detects any invalid input? -> A: Artifact build is atomic; any error prevents a valid artifact result and leaves no partially valid artifact.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build Folder Artifact (Priority: P1)

A deployment author can turn a manifest and referenced resource files into a deterministic folder artifact that can be inspected, copied, and later consumed by the deployment engine.

**Why this priority**: This proves the next step in the Phase 1 loop after manifest normalization and gives later engine, CLI, and API slices a concrete artifact contract to consume.

**Independent Test**: Can be tested by building an artifact from a sample manifest and workspace directory, then verifying that the artifact metadata, manifest snapshot, resource payloads, and checksums are present and deterministic.

**Acceptance Scenarios**:

1. **Given** a valid manifest with workflow and recipe paths, **When** the artifact builder writes a folder artifact, **Then** the folder contains an artifact metadata document, the source manifest, referenced payload files, and a checksum inventory.
2. **Given** the same input files are built twice, **When** artifact metadata is compared excluding build timestamp, **Then** artifact identity and content digests are stable.
3. **Given** a manifest references a file outside the workspace root, **When** artifact building runs, **Then** no artifact is produced and diagnostics identify the invalid reference.

---

### User Story 2 - Read And Inspect Artifact (Priority: P2)

A deployment tool can read an existing folder or ZIP artifact and inspect its manifest, resources, metadata, and checksum status without applying it to an environment.

**Why this priority**: Inspection is required before validation, dry-run, API upload, and CLI automation can safely consume artifacts.

**Independent Test**: Can be tested by loading a known artifact and verifying that inspection returns normalized metadata, resource entries, and checksum verification results without requiring a deployment target.

**Acceptance Scenarios**:

1. **Given** a valid folder artifact, **When** it is read, **Then** inspection returns the artifact identity, manifest metadata, resources, and checksum status.
2. **Given** a valid ZIP artifact, **When** it is read, **Then** inspection returns the same logical result as reading the equivalent folder artifact.
3. **Given** an artifact payload file has been modified after build, **When** inspection verifies checksums, **Then** diagnostics report the mismatch and mark the artifact invalid.

---

### User Story 3 - Preserve Artifact Boundaries (Priority: P3)

A platform maintainer can rely on the artifact package to remain portable and separated from reconciliation, hosting, transport, and enterprise packaging features.

**Why this priority**: The artifact contract must be stable enough for future engine, CLI, API, GitOps, and operator slices without pulling in deferred Phase 2 or Phase 3 concerns.

**Independent Test**: Can be tested through dependency-boundary checks and contract tests that prove the package does not reference engine, CLI, API, Kubernetes, OCI, signing, policy, or runtime-state packages.

**Acceptance Scenarios**:

1. **Given** the artifact package is built, **When** dependency-boundary tests inspect references, **Then** only deployment abstractions, manifest contracts, and required serialization/archive dependencies are allowed.
2. **Given** future consumers need artifact information, **When** they use public artifact contracts, **Then** they can inspect immutable artifact metadata without depending on file-system implementation details.

### Edge Cases

- Missing manifest file in an artifact must produce structured diagnostics rather than an unhandled exception.
- Missing referenced payload files must fail artifact build before any partial artifact is considered valid.
- Duplicate payload paths that normalize to the same artifact path must be rejected.
- Artifact readers must reject path traversal entries in archives.
- Unsupported artifact version values must produce diagnostics and avoid best-effort interpretation.
- Empty artifacts and artifacts without any deployable resources must be readable but invalid for apply-oriented consumers.
- Checksum verification must distinguish missing files from changed files.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST build a folder artifact from a parsed deployment manifest and a workspace root.
- **FR-002**: The system MUST include a manifest snapshot, artifact metadata, payload files, and a checksum inventory in each built artifact.
- **FR-003**: The system MUST compute stable content digests for artifact metadata, manifest content, and payload files.
- **FR-004**: The system MUST assign each artifact an immutable identity derived from artifact content rather than target environment or transport.
- **FR-005**: The system MUST read and inspect folder artifacts without requiring a deployment engine or deployment target.
- **FR-006**: The system MUST read and inspect ZIP artifacts with the same logical inspection result as equivalent folder artifacts.
- **FR-007**: The system MUST verify artifact checksums during inspection and report structured diagnostics for missing, changed, or unexpected content.
- **FR-008**: The system MUST prevent path traversal when collecting workspace files and when reading archive entries.
- **FR-009**: The system MUST expose artifact diagnostics through the same diagnostic model used by deployment abstractions.
- **FR-010**: The system MUST preserve control-plane/data-plane separation by packaging only manifest-declared deployable resources and not workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.
- **FR-011**: The system MUST keep ZIP, OCI, NuGet, signature, policy, engine, apply, CLI, API, GitOps, and operator concerns separated so the artifact package can be reused by later slices.
- **FR-012**: The system MUST provide public contracts for artifact building, reading, inspection, metadata, checksum entries, and artifact layout versioning.
- **FR-013**: The system MUST use SHA-256 checksums for Phase 1 while retaining an explicit checksum algorithm field.
- **FR-014**: The system MUST mark artifact metadata with layout version `platform.elsa.io/deployment-artifact/v1alpha1`.
- **FR-015**: The system MUST treat artifact build as atomic: any error prevents a successful artifact result and no partial output may be reported as valid.

### Key Entities *(include if feature involves data)*

- **Deployment Artifact**: Immutable package containing manifest snapshot, payload files, metadata, and checksum inventory.
- **Artifact Metadata**: Versioned descriptor with artifact identity, creation metadata, source manifest metadata, resource summary, and content digest.
- **Artifact Entry**: Logical item inside an artifact, such as manifest, metadata, checksum inventory, or payload file.
- **Checksum Entry**: Digest record for one artifact entry, including path, algorithm, digest value, size, and entry kind.
- **Artifact Inspection Result**: Read-only view of artifact metadata, manifest snapshot, resources, checksum verification status, and diagnostics.
- **Artifact Diagnostic**: Structured validation message emitted while building, reading, or verifying an artifact.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A valid sample manifest with two referenced payload files can be built into a folder artifact and inspected successfully in one test flow.
- **SC-002**: The same valid sample artifact can be represented as both folder and ZIP formats with equivalent logical inspection results.
- **SC-003**: Rebuilding unchanged inputs produces the same artifact content identity in 100% of deterministic build tests.
- **SC-004**: Tampering with any payload file produces a checksum diagnostic that identifies the affected artifact path.
- **SC-005**: Path traversal attempts from workspace paths or archive entries are rejected in automated tests.
- **SC-006**: Dependency-boundary tests prove the artifact package has no references to engine, CLI, API, Kubernetes, OCI, signing, policy, hosting, persistence, or runtime-state packages.

## Assumptions

- Phase 1 artifact packaging starts with folder and ZIP formats only.
- Artifact build input is a parsed manifest plus a workspace root; command-line and HTTP entry points arrive in later slices.
- Artifact identity is content-derived and environment-neutral.
- The artifact package may depend on deployment abstractions and manifest packages, plus serialization/archive primitives needed for artifact IO.
- Raw secrets are out of scope; future secret references must be represented as references, not embedded secret values.
- Artifact creation, validation, dry-run, apply, and history remain separate slices even though this package produces input for those later steps.
