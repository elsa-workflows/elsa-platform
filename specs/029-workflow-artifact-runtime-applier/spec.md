# Feature Specification: Workflow Artifact Runtime Applier

**Feature Branch**: `029-workflow-artifact-runtime-applier`

**Created**: 2026-05-28

**Status**: Draft

**Input**: User description: "Create the Elsa Workflows runtime integration package that consumes Valence Control deployment commands, validates workflow artifacts, applies supported workflow definition artifacts to the local runtime store, and reports safe progress and outcomes back to Valence Control."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Runtime Applies Workflow Artifact (Priority: P1)

A runtime operator installs the Valence Control integration for Elsa Workflows so a runtime can poll Valence Control, claim a deployment command, fetch the artifact payload, validate it, and install the workflow definition into the local runtime store.

**Why this priority**: This is the first artifact consumer and completes the deploy path from Valence Control command to runtime application.

**Independent Test**: Register a runtime with workflow artifact capabilities, queue a deployment command for an `elsa.workflow-definition` artifact, run the sync worker, and verify the workflow definition is installed exactly once.

**Acceptance Scenarios**:

1. **Given** a runtime supports `elsa.workflow-definition`, **When** it claims a compatible deployment command, **Then** it validates and installs the workflow definition artifact.
2. **Given** the same command is delivered twice, **When** the runtime sees the same idempotency key and digest, **Then** it does not apply duplicate state.
3. **Given** the artifact digest does not match the payload, **When** the runtime validates the payload, **Then** it rejects the command and reports safe diagnostics.

---

### User Story 2 - Report Progress And Results (Priority: P1)

Valence Control users can see runtime apply progress and final status without Valence Control understanding workflow internals.

**Why this priority**: The Valence Control deployment console must remain the operator-facing source of truth while the runtime owns interpretation and apply.

**Independent Test**: Run a deployment that reports validation, apply progress, success, failure, and rejection states, then verify Valence Control run history shows safe events.

**Acceptance Scenarios**:

1. **Given** a runtime is applying an artifact, **When** validation and install stages progress, **Then** the runtime heartbeats and posts safe progress.
2. **Given** apply succeeds, **When** the runtime completes the command, **Then** Valence Control records observed digest and runtime reference.
3. **Given** apply fails, **When** the runtime reports failure, **Then** Valence Control stores safe diagnostics and marks the run failed.

---

### User Story 3 - Advertise Runtime Compatibility (Priority: P2)

The runtime integration advertises artifact type support, schema compatibility, and required capabilities so Valence Control validation can target commands correctly.

**Why this priority**: Valence Control should route workflow artifacts only to runtimes that can interpret them.

**Independent Test**: Register heartbeat capabilities and verify Valence Control validation accepts compatible artifacts and blocks incompatible artifact/schema combinations.

**Acceptance Scenarios**:

1. **Given** the runtime integration starts, **When** it sends heartbeat metadata, **Then** Valence Control records workflow artifact capability support.
2. **Given** a command targets an unsupported artifact type, **When** the runtime polls, **Then** it rejects safely or does not receive the command after validation.

### Edge Cases

- Runtime loses its lease during a long apply.
- Runtime restarts after applying locally but before completing the Valence Control command.
- Payload reference is unavailable, unauthorized, corrupted, too large, or digest-mismatched.
- Artifact schema is newer than the installed runtime integration supports.
- Local workflow store rejects the definition due to validation, conflict, or storage error.
- Diagnostics include raw workflow content, credentials, tokens, or connection strings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an opt-in Elsa Workflows runtime integration package.
- **FR-002**: Runtime integration MUST poll Valence Control runtime command APIs and claim commands with a lease.
- **FR-003**: Runtime integration MUST support artifact type `elsa.workflow-definition` and advertise that support through runtime registration or heartbeat metadata.
- **FR-004**: Runtime integration MUST fetch artifact payloads through approved payload references and verify content digest before apply.
- **FR-005**: Runtime integration MUST validate artifact schema compatibility before writing to the local workflow runtime store.
- **FR-006**: Runtime integration MUST apply workflow artifacts exactly once per idempotency key and content digest.
- **FR-007**: Runtime integration MUST post heartbeat, progress, completion, failure, or rejection to Valence Control using the active lease token.
- **FR-008**: Runtime integration MUST report observed artifact digest and runtime reference after successful apply.
- **FR-009**: Runtime integration MUST redact or reject raw workflow content, credentials, tokens, connection strings, and secret values from diagnostics.
- **FR-010**: Runtime integration MUST fail closed when payload retrieval, digest verification, schema validation, lease ownership, or local apply cannot be proven safe.

### Key Entities *(include if feature involves data)*

- **Runtime Sync Worker**: Runtime-side background process that polls Valence Control and owns command claim/heartbeat/completion calls.
- **Workflow Artifact Applier**: Runtime component that validates and installs workflow definition artifacts into the local runtime store.
- **Apply Journal**: Runtime-local record used to prevent duplicate apply for the same command idempotency key and digest.
- **Runtime Apply Result**: Safe status, observed digest, runtime reference, and diagnostics reported to Valence Control.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A compatible runtime applies a valid workflow artifact command exactly once in automated integration tests.
- **SC-002**: Duplicate command delivery does not create duplicate workflow definitions or duplicate runtime side effects.
- **SC-003**: Digest mismatch, unsupported schema, and local validation failure all produce safe Valence Control-visible rejection or failure states.
- **SC-004**: Runtime progress appears in Valence Control run history within 10 seconds during the integration test.
- **SC-005**: Automated tests prove diagnostics do not include raw workflow payloads, tokens, passwords, connection strings, or secret values.

## Assumptions

- Valence Control runtime command APIs and artifact envelope metadata already exist.
- Runtime owns workflow definition storage and execution semantics.
- The first implementation may use a simple polling worker before provider-specific webhook triggers are added.
- Direct push transports remain optional and out of scope for the first runtime applier slice.
