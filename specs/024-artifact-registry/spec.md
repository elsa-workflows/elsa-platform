# Feature Specification: Deployment Artifact Registry

**Feature Branch**: `024-artifact-registry`

**Created**: 2026-05-26

**Status**: Draft

**Input**: User description: "Add a workspace-scoped deployment artifact registry and console inspection slice. Users can register already-built folder or ZIP deployment artifacts by metadata/reference, inspect immutable artifact metadata and checksum status through authorized workspace APIs, and open a real Artifacts console view instead of the placeholder. This must fit the deployment PRD loop manifest -> artifact -> validation -> dry-run -> apply -> history, reuse existing Deployment.Artifacts contracts where appropriate, store metadata and provider/file references only, never store raw artifact payloads or secrets in the catalog database, and must not implement OCI, signing, GitOps, provider apply, live runtime drift, or object storage upload in this slice."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register Artifact Metadata (Priority: P1)

A workspace member with deployment setup permission registers an already-built deployment artifact by providing immutable artifact metadata and a storage/file reference so the platform can track the artifact without storing its payload in the catalog database.

**Why this priority**: The deployment vision depends on artifacts as the handoff between manifest authoring and validation/dry-run/apply. A metadata registry is the smallest workspace API slice that makes artifacts real in the platform console without introducing storage uploads, OCI, signing, or provider apply.

**Independent Test**: Register an artifact for a workspace, reload the artifact list, and verify the artifact identity, layout version, digest, source manifest metadata, resource summary, reference, and registration actor are persisted without raw payload or secret values.

**Acceptance Scenarios**:

1. **Given** a workspace member with setup permission and artifact metadata from a valid folder or ZIP artifact, **When** they register the artifact, **Then** the platform stores a workspace-owned artifact record with immutable identity, digest, layout version, source manifest summary, resource counts, reference metadata, and audit timestamps.
2. **Given** the same artifact identity is registered again in the same workspace, **When** the request is submitted, **Then** the platform rejects the duplicate or returns the existing record without creating conflicting metadata.
3. **Given** a workspace member lacks setup permission, **When** they attempt artifact registration, **Then** the mutation is rejected while read access remains governed by deployment read permission.

---

### User Story 2 - Inspect Registered Artifacts (Priority: P1)

A workspace member opens Artifacts and inspects registered deployment artifacts, including immutable metadata, resource summaries, checksum status, and safe diagnostics.

**Why this priority**: Users need to understand what artifact would later be validated, dry-run, or applied. Inspection closes the current console gap where the Artifacts navigation exists but is disabled.

**Independent Test**: Seed registered artifacts in a workspace, open the Artifacts console view, and verify only that workspace's artifacts and details render with safe diagnostics and no raw payload content.

**Acceptance Scenarios**:

1. **Given** registered artifacts exist in a workspace, **When** a member opens Artifacts, **Then** the console lists artifact identity, source manifest name/version, resource count, checksum status, registration time, and current diagnostics.
2. **Given** a member selects an artifact, **When** detail is shown, **Then** immutable metadata, reference metadata, checksum verification status, resource summaries, and safe diagnostics are visible.
3. **Given** two workspaces have different artifacts, **When** a member requests another workspace's artifact, **Then** the platform denies access without exposing cross-workspace metadata.

---

### User Story 3 - Refresh Artifact Inspection Status (Priority: P2)

A workspace member with setup permission refreshes inspection status for a registered artifact reference so checksum and diagnostic metadata can be updated from the referenced artifact source.

**Why this priority**: Registry metadata can become stale if referenced artifact content changes or becomes unavailable. Refreshing inspection state provides an explicit safety check before later validation and dry-run slices.

**Independent Test**: Register an artifact with a readable test reference, refresh inspection, and verify checksum status and diagnostics update. Register an unavailable or mismatched reference and verify the artifact is marked invalid without losing immutable identity.

**Acceptance Scenarios**:

1. **Given** a registered artifact has a readable reference, **When** an authorized user refreshes inspection, **Then** the platform updates checksum status, inspected time, resource summary, and safe diagnostics.
2. **Given** a referenced artifact is missing or checksum verification fails, **When** inspection refresh runs, **Then** the platform marks the artifact invalid with safe diagnostics and does not alter the recorded immutable artifact identity.
3. **Given** refresh omits payload upload, **When** the operation completes, **Then** only metadata and diagnostics are persisted.

### Edge Cases

- Artifact metadata references a layout version other than `platform.elsa.io/deployment-artifact/v1alpha1`.
- Artifact identity or digest does not match the referenced artifact during refresh.
- Artifact reference is malformed, inaccessible, or points outside the allowed local/test reference root.
- Artifact diagnostics contain file paths or provider errors; responses must keep them safe and avoid raw secret values.
- Artifact registration attempts to include raw payload, manifest JSON, workflow content, token, password, or secret value fields.
- Concurrent users register the same artifact identity in the same workspace.
- A direct API caller submits a cross-workspace artifact ID.
- The Artifacts console is opened for a workspace with no registered artifacts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow authorized workspace users to register deployment artifact metadata and a provider/file reference for a workspace.
- **FR-002**: System MUST persist artifact identity, layout version, content digest, source manifest summary, resource summary, checksum status, inspection status, safe diagnostics, artifact reference, registration actor, and timestamps.
- **FR-003**: System MUST never store raw artifact payloads, workflow definitions, manifest JSON, provider tokens, raw credentials, or secret values in catalog artifact records.
- **FR-004**: System MUST enforce workspace membership and deployment permissions for every artifact read, registration, and refresh operation.
- **FR-005**: System MUST expose a workspace-scoped artifact list and artifact detail API.
- **FR-006**: System MUST reject duplicate artifact identities within the same workspace unless the submitted metadata matches the existing record and the response is idempotent.
- **FR-007**: System MUST reject or mark invalid artifact metadata with unsupported layout versions, missing identity, missing digest, malformed reference, or unsafe diagnostic content.
- **FR-008**: System MUST support an explicit inspection refresh operation that updates checksum status, inspection timestamp, resource summary, and safe diagnostics from the referenced artifact when a supported local/test reference is available.
- **FR-009**: System MUST preserve the recorded artifact identity during inspection refresh, even when the referenced artifact is missing or mismatched.
- **FR-010**: System MUST expose a real Artifacts console view available from navigation for authenticated workspace users.
- **FR-011**: Console users MUST be able to view artifact list, empty, loading, unauthorized, invalid, and detail states without relying on sample data.
- **FR-012**: Console users with setup permission MUST be able to register artifact metadata and refresh inspection status through live workspace APIs.
- **FR-013**: Artifact records MUST be usable as future inputs to validation, dry-run, apply, and history slices without implementing those actions in this slice.
- **FR-014**: System MUST stay within metadata registry and inspection scope; OCI, signing, GitOps, provider apply, object storage upload, live runtime drift, and external approval workflows are out of scope.

### Key Entities *(include if feature involves data)*

- **Deployment Artifact Record**: Workspace-owned registry entry containing immutable artifact identity, digest, layout version, manifest summary, resource summary, reference metadata, checksum/inspection state, diagnostics, and audit metadata.
- **Artifact Reference**: Provider/file reference that points to where the artifact payload can be inspected later without storing the payload in the catalog database.
- **Artifact Inspection State**: Latest verification summary for checksum status, validity, diagnostics, inspected timestamp, and resource counts.
- **Artifact Diagnostic**: Safe structured message describing registration or inspection issues without raw payload content or secret values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workspace member with setup permission can register artifact metadata through API and console and see it in the Artifacts list after refresh.
- **SC-002**: API tests prove members cannot read, register, or refresh artifacts outside their workspace.
- **SC-003**: Automated tests prove artifact records and console output do not contain raw payloads, manifest JSON, workflow definitions, tokens, passwords, or secret values.
- **SC-004**: Duplicate artifact identity handling is deterministic and cannot create conflicting records in one workspace.
- **SC-005**: Inspection refresh marks missing or checksum-mismatched references invalid while preserving immutable identity metadata.
- **SC-006**: The Artifacts navigation no longer shows the feature as disabled and the page renders live empty, list, detail, and error states.
- **SC-007**: Artifact list loading for a normal workspace with 250 registered artifacts completes in under 3 seconds in the integration test environment.

## Assumptions

- Existing workspace identity, deployment permissions, and deployment cockpit authorization remain the foundation.
- Existing `Elsa.Platform.Deployment.Artifacts` contracts are the source of truth for artifact layout, metadata, checksum, and inspection concepts where they fit.
- The first refresh implementation may support local/test filesystem references only; cloud/object storage and OCI references are future provider-specific slices.
- Artifact upload and payload storage are out of scope; the catalog database stores metadata and references only.
- Validation, dry-run, apply, artifact signing, GitOps promotion, and external approval workflows are future slices.
