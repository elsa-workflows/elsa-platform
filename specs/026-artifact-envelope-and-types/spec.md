# Feature Specification: Artifact Envelope And Types

**Feature Branch**: `026-artifact-envelope-and-types`

**Created**: 2026-05-28

**Status**: Draft

**Input**: User description: "Create the shared artifact envelope and artifact type model that lets Elsa Platform accept deployable artifacts from Studio, CLI, CI, and future producers while remaining agnostic about artifact internals. The envelope must carry immutable identity, type identifiers such as `elsa.workflow-definition`, producer metadata, payload references, digest rules, safe display metadata, and compatibility hints without storing raw payload content or secrets in catalog tables."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit Typed Artifact Envelope (Priority: P1)

A producer integration submits a typed artifact envelope to the platform so the artifact registry can store a producer-neutral record that is still specific enough for later validation, promotion, and runtime application.

**Why this priority**: Studio, CLI, CI, and manual registration need one shared handoff contract. Without a typed envelope, later deployment command and runtime sync work cannot determine which runtime integration should apply an artifact.

**Independent Test**: Submit an envelope for `elsa.workflow-definition` with safe metadata, producer details, payload reference, digests, and compatibility hints, then verify the platform stores the envelope metadata without raw payload content or secrets.

**Acceptance Scenarios**:

1. **Given** a producer has built an immutable workflow artifact, **When** it submits an envelope with type `elsa.workflow-definition`, **Then** the platform stores the artifact type, schema version, identity, digests, producer metadata, safe display metadata, payload reference, and compatibility hints.
2. **Given** a producer submits the same envelope identity again, **When** the envelope matches the existing record, **Then** the platform treats the request as idempotent and does not create a conflicting artifact.
3. **Given** a producer submits a conflicting envelope for an existing identity, **When** any immutable field differs, **Then** the platform rejects the submission.

---

### User Story 2 - Manage Artifact Type Semantics (Priority: P1)

Platform operators and integration authors rely on a stable artifact type catalog so producers and runtime appliers can agree on artifact meaning without the platform needing to understand every payload.

**Why this priority**: Artifact type IDs are the contract between producers, Platform, and runtime integrations. They must be stable, discoverable, and constrained before runtime command sync is introduced.

**Independent Test**: Register or load the built-in `elsa.workflow-definition` artifact type and verify envelopes that reference unknown, disabled, or incompatible artifact types fail closed.

**Acceptance Scenarios**:

1. **Given** the built-in workflow artifact type exists, **When** a valid envelope references `elsa.workflow-definition`, **Then** the platform accepts the type and records its declared schema version.
2. **Given** an envelope references an unknown artifact type, **When** it is submitted, **Then** the platform rejects the envelope before storing it.
3. **Given** a runtime environment advertises supported artifact types, **When** an artifact has no compatible target hint, **Then** deployment validation can flag the mismatch before apply.

---

### User Story 3 - Preserve Safe Metadata Boundaries (Priority: P2)

A workspace user can search, inspect, and compare artifacts using safe metadata while the platform continues to avoid catalog storage of payloads, workflow definitions, credentials, or secrets.

**Why this priority**: The platform UI needs useful artifact details, but the architecture requires payload opacity and safe metadata boundaries.

**Independent Test**: Submit envelopes with labels, annotations, source references, compatibility hints, and diagnostics, then verify secret-like keys and raw payload fields are rejected or redacted from persistence and API responses.

**Acceptance Scenarios**:

1. **Given** an envelope includes safe display labels and annotations, **When** the artifact is listed, **Then** users can identify the artifact without seeing raw payload content.
2. **Given** metadata includes secret-like keys or values, **When** the envelope is validated, **Then** the platform rejects or redacts unsafe fields before persistence.
3. **Given** a payload reference points to external storage, **When** the artifact is inspected, **Then** only reference metadata, digest status, and safe diagnostics are exposed.

---

### User Story 4 - Maintain Backward Compatibility (Priority: P3)

Existing manually registered deployment artifacts continue working while the registry gains typed envelopes and producer metadata.

**Why this priority**: The current artifact registry is already implemented and tested. The envelope upgrade must preserve existing metadata registration and console flows.

**Independent Test**: Register an existing folder or ZIP artifact through the current API and verify it is represented as an envelope-backed artifact with default producer and artifact type metadata.

**Acceptance Scenarios**:

1. **Given** an existing artifact record lacks envelope fields, **When** it is read after the upgrade, **Then** the platform projects it with default type and producer metadata without breaking the UI.
2. **Given** a manual artifact registration request uses the existing metadata shape, **When** the request is accepted, **Then** the platform creates an envelope-compatible artifact record.
3. **Given** existing artifact tests assert no payload or secrets are stored, **When** the envelope fields are added, **Then** those safety guarantees still hold.

### Edge Cases

- Envelope references an unknown, disabled, or malformed artifact type ID.
- Envelope type and payload reference disagree, such as a workflow type with a generic deployment manifest payload.
- Producer metadata includes credentials, tokens, webhook signatures, or raw authorization headers.
- Compatibility hints are too broad, empty, malformed, or conflict with artifact type requirements.
- Payload digest, manifest digest, and envelope digest use unsupported algorithms or mismatched values.
- A legacy artifact record has no explicit type or producer metadata.
- Two producers submit the same logical artifact with different immutable digests.
- Runtime command generation attempts to target a runtime that does not advertise the artifact type.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST define a shared artifact envelope contract for all producer integrations.
- **FR-002**: Artifact envelopes MUST include immutable artifact identity, artifact type ID, artifact schema version, envelope version, payload reference, content digest, optional manifest digest, producer metadata, safe display metadata, compatibility hints, diagnostics, and submission audit metadata.
- **FR-003**: System MUST define a stable artifact type ID format and include the built-in `elsa.workflow-definition` artifact type.
- **FR-004**: System MUST validate artifact type IDs before accepting envelopes.
- **FR-005**: System MUST reject unknown or disabled artifact types unless an explicit extension registration allows them.
- **FR-006**: System MUST validate digest algorithm, digest value shape, and immutable identity consistency.
- **FR-007**: System MUST store only envelope metadata, digests, safe diagnostics, and payload references in catalog tables.
- **FR-008**: System MUST NOT store raw payload content, workflow definitions, manifest JSON, credentials, bearer tokens, webhook secrets, connection strings, or raw secret values in catalog tables.
- **FR-009**: System MUST validate safe metadata keys and values and reject or redact secret-like metadata before persistence and API response.
- **FR-010**: System MUST record producer type and producer reference for Studio, CLI, CI, manual registration, and future producers.
- **FR-011**: System MUST allow compatibility hints to describe required artifact type support, runtime family, runtime version range, required capabilities, and optional environment constraints.
- **FR-012**: System MUST expose envelope metadata through workspace artifact list/detail APIs and console views without exposing payload content.
- **FR-013**: System MUST preserve deterministic duplicate handling for repeated envelope submissions.
- **FR-014**: System MUST provide a backward-compatible projection for existing artifact records without explicit envelope metadata.
- **FR-015**: System MUST keep artifact type interpretation outside the platform core except for validation of registered type IDs, safe metadata, references, and compatibility hints.
- **FR-016**: System MUST make envelope records usable by future deployment command, runtime sync, Studio submit, and workflow runtime applier slices.

### Key Entities *(include if feature involves data)*

- **Artifact Envelope**: Producer-neutral immutable metadata wrapper describing artifact identity, type, digests, payload reference, producer, safe metadata, and compatibility hints.
- **Artifact Type Definition**: Stable platform or extension-owned identifier and schema metadata that describes what kind of runtime integration can interpret an artifact.
- **Artifact Producer**: Studio, CLI, CI, manual registration, or another source that creates or submits an artifact envelope.
- **Payload Reference**: Provider-specific pointer to artifact content outside catalog tables, such as local test storage, object storage, OCI, or producer-managed storage.
- **Compatibility Hint**: Safe, structured statement of runtime family, runtime version, capabilities, and artifact type requirements used by validation and command targeting.
- **Envelope Diagnostic**: Safe structured message describing validation or inspection status without leaking raw payload or secret values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A producer can submit a valid `elsa.workflow-definition` envelope and see the artifact listed with type, producer, digest, and safe display metadata.
- **SC-002**: Automated tests prove unknown artifact types, invalid digests, conflicting duplicate envelopes, and unsafe metadata are rejected.
- **SC-003**: Automated persistence tests prove catalog tables do not contain raw payloads, workflow definitions, manifest JSON, tokens, passwords, or connection strings.
- **SC-004**: Existing manual artifact registration tests continue to pass after the envelope model is introduced.
- **SC-005**: Artifact list and detail APIs expose type, producer, compatibility, and submission status for at least 250 artifacts in under 3 seconds in the integration test environment.
- **SC-006**: Runtime command planning can identify whether a runtime advertises support for an artifact type without inspecting artifact payload content.

## Assumptions

- The current workspace artifact registry remains the persistence and API entry point for artifact metadata.
- The first built-in artifact type is `elsa.workflow-definition`; generic deployment package artifacts may be represented as a separate built-in type if needed for backward compatibility.
- Payload content remains outside catalog tables. Upload storage, OCI publication, signing, and content-addressed object storage are future provider-specific slices.
- Producer authentication and Studio UI are covered by later specs; this slice defines the envelope they will submit.
- Runtime application of artifact payloads is covered by later runtime command sync and workflow applier specs.
