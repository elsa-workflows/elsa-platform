# Feature Specification: Artifact Backed Promotion

**Feature Branch**: `030-artifact-backed-promotion`

**Created**: 2026-05-28

**Status**: Draft

**Input**: User description: "Make promotion, deployment, and rollback artifact-first by having desired-state revisions reference immutable deployable artifacts instead of embedding workflow intent directly, while keeping Platform agnostic about workflow internals."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Promote Artifact-Backed Revision (Priority: P1)

A workspace operator promotes a known artifact-backed revision from one environment to another and can compare what will change before deployment.

**Why this priority**: This turns submitted artifacts into the source of truth for governed promotion across environments.

**Independent Test**: Create a source revision that references a workflow artifact, preview promotion to a target environment, approve confirmation, and verify the target revision references the same immutable artifact identity.

**Acceptance Scenarios**:

1. **Given** a source environment has an artifact-backed revision, **When** the user previews promotion, **Then** Platform compares artifact identity, digest, safe metadata, and environment configuration.
2. **Given** the user confirms promotion, **When** Platform creates the target revision, **Then** the target revision references the immutable artifact rather than copying raw workflow content.
3. **Given** target runtime capabilities do not support the artifact type, **When** promotion is previewed, **Then** Platform blocks or warns before deployment.

---

### User Story 2 - Deploy Artifact-Backed Revision (Priority: P1)

A deployment run creates a runtime command that references the artifact-backed desired-state revision and sends only safe artifact/revision references to the runtime.

**Why this priority**: Deployment command sync should carry durable intent, not raw workflow payloads.

**Independent Test**: Queue deployment from an artifact-backed revision and verify the command references artifact identity/digest and no raw workflow definition content is persisted in command or run records.

**Acceptance Scenarios**:

1. **Given** a target revision references a workflow artifact, **When** deployment is queued, **Then** the runtime command includes artifact reference, revision reference, target engine, and idempotency key.
2. **Given** the artifact has unsafe or unavailable payload reference metadata, **When** deployment validation runs, **Then** deployment is blocked before command claim.

---

### User Story 3 - Roll Back To Known Artifact (Priority: P2)

A workspace operator rolls back an environment to a previously successful artifact-backed revision.

**Why this priority**: Rollback must redeploy known-good immutable artifacts instead of relying on mutable runtime state.

**Independent Test**: Deploy revision A, deploy revision B, request rollback to revision A, and verify Platform queues a command for revision A's artifact identity and records rollback history.

**Acceptance Scenarios**:

1. **Given** a previous successful deployment exists, **When** rollback is requested, **Then** Platform selects the known-good artifact-backed revision.
2. **Given** the artifact referenced by the rollback revision is missing or invalid, **When** rollback validation runs, **Then** rollback is blocked with safe diagnostics.

### Edge Cases

- Artifact record is deleted, archived, inaccessible, or belongs to another workspace.
- Source and target environments use different configuration overlays.
- Promotion attempts to cross from a non-promotable tier or into a protected tier without required confirmation.
- Runtime capabilities changed after preview but before deployment.
- Rollback target artifact exists but payload reference is no longer retrievable.
- Desired-state revision references multiple artifacts with mixed compatibility.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Desired-state revisions MUST be able to reference immutable workspace artifact records.
- **FR-002**: Promotion preview MUST compare artifact identity, digest, type, safe metadata, and environment-specific configuration without reading raw workflow payloads.
- **FR-003**: Promotion MUST create target revisions that reference artifacts instead of embedding raw workflow definitions.
- **FR-004**: Deployment command creation MUST include safe artifact references for artifact-backed revisions.
- **FR-005**: Platform MUST validate artifact existence, workspace ownership, digest metadata, artifact type compatibility, and runtime capability hints before deployment.
- **FR-006**: Platform MUST NOT store raw workflow definition content, payload content, credentials, tokens, connection strings, or secret values in desired-state, command, run, or history records.
- **FR-007**: Rollback MUST redeploy a known-good artifact-backed revision and preserve rollback source history.
- **FR-008**: Tier capability safeguards MUST continue to control promotion and deployment eligibility.
- **FR-009**: Runtime capability safeguards MUST continue to control technical artifact compatibility.
- **FR-010**: Existing structured desired-state records MUST remain readable or migratable during the transition.

### Key Entities *(include if feature involves data)*

- **Artifact-Backed Desired-State Revision**: Revision that references one or more immutable artifact records and optional environment configuration overlays.
- **Artifact Promotion Preview**: Comparison of source and target artifact references, safe metadata, configuration overlays, tier policy, and runtime compatibility.
- **Artifact Deployment Command Reference**: Safe command payload pointing to artifact identity, digest, type, revision, and target runtime.
- **Artifact Rollback Target**: Previously successful artifact-backed revision selected for rollback.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can promote an artifact-backed workflow revision across environments without raw workflow content entering catalog tables.
- **SC-002**: Deployment commands generated from artifact-backed revisions include artifact references and pass existing runtime command API tests.
- **SC-003**: Rollback to a previous artifact-backed revision queues the expected artifact identity in automated tests.
- **SC-004**: Cross-workspace artifacts, missing artifacts, unsupported artifact types, and unsafe metadata all fail closed before deployment.
- **SC-005**: Promotion preview for a normal workspace dataset completes in under 3 seconds in the integration test environment.

## Assumptions

- Artifact envelope/type registry and runtime command sync are already implemented.
- Runtime application remains the responsibility of runtime applier packages.
- The first slice can support workflow artifact references while keeping existing structured records readable.
- Environment-specific configuration overlays may be minimal initially and expanded later.
