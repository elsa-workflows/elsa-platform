# Feature Specification: Runtime Command Sync

**Feature Branch**: `028-runtime-command-sync`

**Created**: 2026-05-28

**Status**: Draft

**Input**: User description: "Create the platform deployment command contract and runtime sync API so external runtime integrations can pull deployment work from Elsa Platform, claim commands with a lease, report progress, complete, fail, or reject work, and avoid duplicate apply. Runtime pull/sync is the default transport, webhook-triggered fetch is a notification accelerator, and direct push remains an explicit opt-in. Existing queued deployment runs remain the console-facing source of truth."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Runtime Polls And Claims Commands (Priority: P1)

A registered runtime integration polls the platform for pending deployment commands that target its engine, claims exactly one command with a lease, and receives the artifact/run metadata needed to validate and apply the work.

**Why this priority**: Runtime pull/sync is the default deployment transport. The platform must be able to hand work to runtimes without requiring inbound runtime endpoints.

**Independent Test**: Queue a deployment run that creates a pending command, poll as the target runtime, claim the command, and verify no second worker can claim the same command until the lease expires or the command is released.

**Acceptance Scenarios**:

1. **Given** a deployment run has a pending command for a registered engine, **When** that engine's runtime sync worker polls, **Then** the platform returns only commands scoped to that workspace and engine.
2. **Given** a runtime sync worker claims a pending command, **When** another worker polls or attempts to claim it, **Then** the command is not delivered for duplicate apply while the lease is active.
3. **Given** a command is claimed, **When** the platform returns the command payload, **Then** it includes safe run metadata, action, artifact or revision reference, idempotency key, lease expiration, and no raw secrets.

---

### User Story 2 - Runtime Reports Progress And Completion (Priority: P1)

A runtime sync worker reports progress, heartbeat, validation results, apply results, and final completion or failure so the platform deployment run history remains authoritative for users.

**Why this priority**: The console and audit trail must stay correct even though the runtime applies the artifact outside the platform process.

**Independent Test**: Claim a command, post progress and heartbeat updates, complete the command, and verify the linked deployment run moves through running to succeeded with append-only history and safe diagnostics.

**Acceptance Scenarios**:

1. **Given** a command is claimed by a runtime, **When** the worker posts progress, **Then** the platform updates command progress and appends a run history event.
2. **Given** a runtime completes a command with observed artifact digest and runtime reference, **When** the completion is accepted, **Then** the command is marked completed and the deployment run is marked succeeded or rolled back according to the action.
3. **Given** a runtime fails or rejects a command, **When** it reports safe diagnostics, **Then** the command and run are marked failed or rejected without exposing raw secrets.

---

### User Story 3 - Recover Stale Or Duplicate Deliveries (Priority: P2)

The platform prevents duplicate apply and makes stale commands visible for operator/user recovery instead of automatically replaying potentially unsafe work.

**Why this priority**: Runtime pull, webhook notifications, process restarts, and network retries can all deliver the same command more than once. Deployment safety depends on idempotency and explicit recovery semantics.

**Independent Test**: Claim a command, let the lease become stale, poll again, and verify the command moves to recovery-required or reclaimable according to policy without silently applying twice. Send duplicate webhook notifications and duplicate completion calls and verify state remains deterministic.

**Acceptance Scenarios**:

1. **Given** a claimed command misses its heartbeat past the stale threshold, **When** recovery processing runs, **Then** the command is marked recovery-required or made explicitly reclaimable with a new attempt record.
2. **Given** a webhook notification is delivered more than once, **When** the runtime fetches commands, **Then** the same command idempotency key prevents duplicate apply.
3. **Given** a runtime posts completion twice for the same command and lease, **When** the second completion is received, **Then** the platform returns the existing final state without appending conflicting history.

---

### User Story 4 - Trigger Fetch Via Webhook Without Authority Transfer (Priority: P3)

The platform can notify a runtime that work is available, but the runtime must fetch and claim the authoritative command from the platform before acting.

**Why this priority**: Webhooks can reduce polling latency while preserving the pull/sync security and consistency model.

**Independent Test**: Emit a webhook notification for a pending command, verify the notification contains only safe trigger metadata, and verify the runtime still must call poll/claim before command details are returned.

**Acceptance Scenarios**:

1. **Given** a runtime has webhook notifications enabled, **When** a command becomes pending, **Then** the platform emits a safe notification containing workspace, engine, and command hint metadata.
2. **Given** a webhook notification is received, **When** the runtime acts, **Then** it must poll or claim the command through the command API before applying anything.
3. **Given** webhook delivery fails, **When** the runtime continues periodic polling, **Then** the command remains discoverable and deployable.

### Edge Cases

- Runtime polls with an identity that is not authorized for the target workspace or engine.
- Runtime claims a command that is already leased by another worker.
- Runtime heartbeat arrives after the lease expires.
- Runtime completion references a different command, run, lease token, artifact digest, or engine than the claim.
- Command payload references an unsupported artifact type or stale artifact digest.
- Command diagnostics contain raw payload content, tokens, connection strings, or secret values.
- Platform API restarts while commands are pending, claimed, or completing.
- Webhook notification is delayed, duplicated, lost, or delivered before the runtime has connectivity.
- Direct push is configured for a runtime that also polls; idempotency still prevents duplicate apply.
- A deployment run is cancelled or recovery-required while a runtime attempts to complete the command.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST create durable deployment command records linked to deployment runs.
- **FR-002**: Deployment commands MUST include workspace ID, run ID, target environment, target engine, action, artifact or revision reference, idempotency key, status, attempt number, lease metadata, expiration metadata, progress, and safe diagnostics.
- **FR-003**: System MUST expose runtime-facing APIs to poll pending commands, claim a command, heartbeat a claim, report progress, complete, fail, reject, and read final command state.
- **FR-004**: Runtime command APIs MUST authorize the caller for the target workspace and engine before returning command metadata.
- **FR-005**: System MUST ensure only one active lease can claim a command at a time.
- **FR-006**: System MUST require a lease token for heartbeat, progress, complete, fail, and reject operations.
- **FR-007**: System MUST prevent duplicate apply by enforcing idempotency keys, lease state, and deterministic final-state handling for repeated runtime calls.
- **FR-008**: System MUST append safe deployment run history events for command creation, claim, progress, completion, failure, rejection, stale recovery, and duplicate delivery handling.
- **FR-009**: System MUST keep deployment run history and status as the console-facing source of truth.
- **FR-010**: System MUST keep existing in-process queued runs compatible by treating the in-process worker as an internal command consumer or by bridging queued runs to commands.
- **FR-011**: System MUST mark stale claimed commands as recovery-required or explicitly reclaimable according to recorded policy; it MUST NOT silently replay runtime apply without a new explicit claim/attempt.
- **FR-012**: System MUST expose webhook notification records or events only as safe command-available triggers; webhook payloads MUST NOT be authoritative deployment commands.
- **FR-013**: System MUST allow polling to remain sufficient when webhook delivery is disabled or fails.
- **FR-014**: System MUST store and return only safe diagnostics, metadata, digests, and references; raw secrets, credentials, payload content, workflow definitions, and connection strings are forbidden.
- **FR-015**: System MUST record runtime-observed artifact digest, runtime reference, validation result, apply result, and final status when a command completes or fails.
- **FR-016**: System MUST preserve workspace isolation for command records, history, API responses, and webhook trigger metadata.
- **FR-017**: System MUST support command actions for deploy, rollback, dry-run/validate where applicable, and future runtime control commands without changing the base command lifecycle.
- **FR-018**: System MUST allow direct push transport only as an explicit runtime configuration mode and still require idempotency and command result reporting.

### Key Entities *(include if feature involves data)*

- **Deployment Command**: Durable work item linked to a deployment run and targeted at a runtime engine.
- **Command Lease**: Time-limited claim that grants one runtime worker authority to process a command attempt.
- **Command Attempt**: Recorded processing attempt with worker identity, claimed time, heartbeat, completion, and outcome metadata.
- **Command Progress Event**: Safe runtime-reported milestone that is also projected into deployment run history.
- **Command Result**: Final validation/apply outcome including observed digest, runtime reference, safe diagnostics, and completion status.
- **Runtime Sync Worker**: Runtime-side integration process that polls, claims, applies, and reports command state.
- **Webhook Notification**: Non-authoritative command-available trigger that tells a runtime to fetch commands from Platform.
- **Idempotency Key**: Stable command key used by runtime and platform to prevent duplicate apply for the same deployment intent.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A runtime sync worker can poll, claim, heartbeat, report progress, and complete a deployment command through authorized APIs.
- **SC-002**: Automated tests prove two workers cannot actively claim the same command at the same time.
- **SC-003**: Automated tests prove duplicate polling, duplicate webhook notification, and duplicate completion do not create duplicate apply or conflicting history.
- **SC-004**: Stale claimed commands transition to recovery-required or explicitly reclaimable state within the configured stale threshold.
- **SC-005**: Deployment run history shows command creation, claim, progress, and final outcome after page refresh.
- **SC-006**: Runtime command APIs and persistence tests prove no raw payload, workflow definition, credential, token, connection string, or secret value is stored or returned.
- **SC-007**: Runtime polling for a normal workspace with 250 pending or historical commands completes in under 3 seconds in the integration test environment.

## Assumptions

- Existing workspace identity, deployment permissions, engine registrations, engine health, and deployment run history remain authoritative.
- Runtime caller authentication can initially reuse trusted test identity/API-key style infrastructure and evolve into package-specific runtime credentials in later integration slices.
- The first implementation may bridge existing queued deployment runs into command records rather than replacing all queue internals at once.
- Runtime command payloads reference artifact envelope metadata and desired-state revisions by ID/digest; they do not embed raw payload content.
- Webhook delivery infrastructure can be represented as records/events in this slice; provider-specific webhook senders can follow later.
- Studio submission and workflow runtime application packages are separate specs and are not implemented here.
