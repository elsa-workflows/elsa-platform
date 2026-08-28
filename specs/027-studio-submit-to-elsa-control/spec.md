# Feature Specification: Studio Submit To Elsa Control

**Feature Branch**: `027-studio-submit-to-elsa-control`

**Created**: 2026-05-28

**Status**: Draft

**Input**: User description: "Create the opt-in Elsa Studio integration package that replaces Elsa Control-integrated authoring handoff with Submit to Elsa Control, packages workflow snapshots as immutable platform artifacts, and keeps direct runtime Publish clearly separate from release, promotion, and deployment."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit Workflow Snapshot (Priority: P1)

A workflow author submits the current workflow definition from a Elsa Control-integrated Elsa Studio installation to Elsa Control as an immutable deployable artifact.

**Why this priority**: This is the producer side of the architecture. Without it, the platform has no first-class workflow artifact handoff from Studio.

**Independent Test**: Configure the Studio integration, submit a workflow definition, and verify Elsa Control stores a typed `elsa.workflow-definition` artifact with safe metadata, producer metadata, digest, and payload reference without deploying it.

**Acceptance Scenarios**:

1. **Given** Studio is connected to Elsa Control, **When** the user chooses **Submit to Elsa Control**, **Then** Elsa Control receives an immutable workflow artifact and the workflow is not deployed or released.
2. **Given** the same workflow snapshot is submitted twice, **When** the artifact identity and digest match, **Then** the operation is idempotent.
3. **Given** Elsa Control rejects the submission, **When** Studio displays the result, **Then** the user sees a safe failure message without leaking credentials or raw payload details.

---

### User Story 2 - Separate Publish From Submit (Priority: P1)

An administrator can configure Elsa Control-integrated Studio so authors do not confuse direct runtime Publish with Elsa Control artifact submission.

**Why this priority**: The product terminology decision depends on a clear UX boundary. Submit means artifact handoff; Publish means direct runtime availability.

**Independent Test**: Enable the Elsa Control integration and verify the handoff command is labeled **Submit to Elsa Control** while any direct runtime Publish path is hidden, disabled, or explicitly separated.

**Acceptance Scenarios**:

1. **Given** the Elsa Control integration is enabled in strict mode, **When** an author opens Studio, **Then** the primary handoff command is **Submit to Elsa Control** and direct Publish is not the primary action.
2. **Given** direct runtime Publish remains enabled by policy, **When** both commands are visible, **Then** the UI distinguishes direct runtime publishing from Elsa Control submission.

---

### User Story 3 - Track Submission State (Priority: P2)

A workflow author can tell whether a workflow snapshot was submitted, already exists, failed validation, or needs credentials.

**Why this priority**: Authors need actionable feedback, but the Elsa Control remains responsible for release, promotion, and deployment.

**Independent Test**: Submit success, duplicate, unauthorized, validation-failed, and unavailable-Elsa Control cases and verify Studio shows correct safe states.

**Acceptance Scenarios**:

1. **Given** a submission succeeds, **When** the author returns to the workflow, **Then** Studio shows the submitted artifact identity and timestamp.
2. **Given** Elsa Control is unavailable, **When** the user submits, **Then** Studio reports a retryable safe error and does not mark the workflow as submitted.

### Edge Cases

- Elsa Control credentials are missing, expired, revoked, or scoped to the wrong workspace.
- A workflow has no stable identifier, has unsaved changes, or contains unsupported schema fields.
- The submit command is invoked while another submission for the same workflow snapshot is in flight.
- Safe metadata contains secret-like keys, bearer tokens, connection strings, or raw credentials.
- Elsa Control accepts artifact metadata but rejects payload reference or digest validation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an opt-in Elsa Studio integration package that adds a **Submit to Elsa Control** command.
- **FR-002**: System MUST package workflow definitions into the shared artifact envelope using artifact type `elsa.workflow-definition`.
- **FR-003**: Submission MUST create an immutable artifact in Elsa Control and MUST NOT release, promote, deploy, publish to runtime, or make the workflow immediately executable.
- **FR-004**: Submission MUST include safe display metadata, source workflow identifiers, producer metadata, schema version, content digest, and payload reference or payload transfer metadata.
- **FR-005**: Submission MUST avoid storing Elsa Control credentials, runtime credentials, bearer tokens, connection strings, or raw secret values in Studio-visible metadata or Elsa Control catalog tables.
- **FR-006**: System MUST make duplicate submissions idempotent when immutable identity and digest match.
- **FR-007**: System MUST reject conflicting duplicate submissions with a safe explanation.
- **FR-008**: Administrators MUST be able to configure whether direct runtime Publish is hidden, disabled, or explicitly shown as separate from **Submit to Elsa Control**.
- **FR-009**: Studio MUST display submission outcomes: submitted, duplicate/no-op, validation failed, unauthorized, unavailable, and retryable error.
- **FR-010**: Submission MUST be workspace-scoped and authorized by Elsa Control identity or a provider-backed credential reference.

### Key Entities *(include if feature involves data)*

- **Studio Submit Configuration**: Workspace, Elsa Control endpoint, authentication mode, publish separation policy, and optional defaults.
- **Workflow Submission Snapshot**: Immutable representation of the workflow definition, source identifiers, schema version, digest, and safe metadata at submit time.
- **Submission Result**: Safe status returned to Studio after Elsa Control accepts, rejects, or cannot process a submission.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can submit a valid workflow snapshot and see a Elsa Control artifact record within 5 seconds in the test environment.
- **SC-002**: Automated tests prove submit does not create a deployment run or mark the workflow executable in a runtime.
- **SC-003**: Automated tests prove duplicate submit is idempotent and conflicting submit fails closed.
- **SC-004**: Automated tests prove unsafe metadata and credentials are rejected or redacted before persistence.
- **SC-005**: At least 90% of common failure cases return an actionable safe Studio status without requiring users to inspect logs.

## Assumptions

- Elsa Studio remains a separate product and runtime Publish behavior is not changed globally.
- The integration is opt-in and can be packaged independently.
- Elsa Control artifact registry and artifact envelope support already exist.
- Artifact payload storage/transfer may be provider-specific; the first slice may use producer-managed or local test references.
