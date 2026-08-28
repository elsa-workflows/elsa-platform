# Feature Specification: Artifact To Engine Deployment

**Feature Branch**: `033-artifact-engine-deploy`

**Created**: 2026-06-07

**Status**: Draft

**Input**: User description: "Create a PRD/spec to define exactly what is needed to allow a registered deployment artifact to be validated, targeted, downloaded, dispatched, applied, and observed on a compatible workflow engine."

## Clarifications

### Session 2026-06-07

- Q: Should deployment create one runtime command per revision, per artifact, or per artifact applier? → A: One command per revision and target engine, containing all artifact records with per-artifact status.
- Q: Should stale or missing engine capability metadata block deployment? → A: Yes; deployment is blocked until the target engine reports current capability metadata.
- Q: How should partial runtime apply be handled after upfront compatibility passes? → A: Mark the run failed with per-artifact outcomes and require operator recovery; do not claim automatic rollback.
- Q: How should runtime artifact downloads be authorized? → A: Runtime downloads require the active command lease for the target engine and command.
- Q: What shape should runtime apply capability IDs use? → A: Use stable artifact-type apply capability IDs in the form `artifact.{artifactTypeId}.apply`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove Artifact And Engine Compatibility Before Deployment (Priority: P1)

A deployment operator opens a desired-state revision and immediately sees whether the selected target engine can apply every artifact referenced by that revision, including the exact missing capability or compatibility reason when deployment is blocked.

**Why this priority**: The current experience exposes a blocker such as "requires runtime capability workflow-definition.apply" without giving the user a complete path to make the artifact deployable. Compatibility must be explicit before any deployment command is queued and should use canonical artifact apply capability IDs such as `artifact.elsa.workflow-definition.apply`.

**Independent Test**: Can be fully tested by registering one workflow-definition artifact, one engine that advertises the required apply capability, and one engine that does not. The compatible engine is deployable; the incompatible engine is blocked with an actionable reason.

**Acceptance Scenarios**:

1. **Given** a revision references an `elsa.workflow-definition` artifact and the target engine advertises `artifact.elsa.workflow-definition.apply`, **When** the operator previews deployment, **Then** the system marks the artifact deployable to that engine.
2. **Given** the target engine does not advertise the required workflow apply capability, **When** the operator selects that engine, **Then** deployment is blocked and the UI explains which capability is missing and how to resolve it.
3. **Given** a revision contains multiple artifact records, **When** compatibility is evaluated, **Then** every artifact must be compatible before the deployment action becomes available.

---

### User Story 2 - Queue A Deployment Command With Downloadable Artifact Payloads (Priority: P1)

A deployment operator deploys a compatible revision to an engine and the platform creates a deployment run plus runtime command that contains safe artifact metadata and a downloadable payload reference, without exposing local filesystem paths or raw artifact contents in the UI.

**Why this priority**: Compatibility alone does not deploy anything. The platform must hand a runtime enough information to fetch and apply the artifact while keeping the console and audit trail authoritative.

**Independent Test**: Can be fully tested by deploying a compatible revision and verifying that a run, command, safe artifact reference, idempotency key, and history event are created without raw payload content or secrets.

**Acceptance Scenarios**:

1. **Given** a compatible revision and target engine, **When** the operator queues deployment, **Then** the platform creates a deployment run and one command targeted to that engine containing all artifact records in the revision.
2. **Given** a command is created, **When** its payload is inspected through authorized runtime APIs, **Then** it includes artifact identity, type, digest, schema, safe display metadata, and a platform download reference.
3. **Given** the registered artifact reference is a local or provider-specific storage path, **When** users view the revision or command, **Then** they see a safe download/action link or display name rather than being asked to copy the raw storage path.

---

### User Story 3 - Runtime Applies The Artifact And Reports Outcome (Priority: P2)

A compatible runtime engine receives the command, downloads the artifact through platform-approved access, verifies the digest, applies the artifact to the local Elsa runtime, and reports progress, success, rejection, or failure back to the deployment run.

**Why this priority**: A platform-side deploy button is not meaningful until the runtime can actually consume the command and make the deployment visible in run history.

**Independent Test**: Can be fully tested by running a runtime sync worker against a queued command and verifying progress events, final run status, observed artifact digest, and runtime reference.

**Acceptance Scenarios**:

1. **Given** a compatible engine has a pending deployment command, **When** its runtime worker claims the command, **Then** it can download the artifact payload and verify its digest before applying.
2. **Given** runtime apply succeeds, **When** the runtime completes the command, **Then** the platform marks the deployment run succeeded and records the observed artifact digest and runtime reference.
3. **Given** runtime apply rejects the artifact because schema, digest, or local validation fails, **When** the runtime reports the outcome, **Then** the platform records a safe diagnostic and marks the run failed or rejected without exposing raw workflow content.

---

### User Story 4 - Guide Users To Resolve Deployment Blockers (Priority: P3)

A user who cannot deploy an artifact because of engine capabilities, missing runtime integration, unavailable artifact payload, tier requirements, or permissions receives a clear explanation and a next action instead of a dead-end error.

**Why this priority**: Blockers are inevitable during setup. The platform should teach the deployment model and reduce support load by showing the specific missing prerequisite.

**Independent Test**: Can be fully tested by setting up common blocked states and verifying each state presents a distinct reason and remediation action.

**Acceptance Scenarios**:

1. **Given** an engine is missing a capability, **When** the operator selects it as a target, **Then** the UI identifies the missing capability and suggests updating or installing the runtime integration.
2. **Given** an artifact payload is unavailable or not downloadable, **When** deployment compatibility is checked, **Then** deployment is blocked with a storage/access reason.
3. **Given** the current user lacks deployment-run permission, **When** they view a compatible revision, **Then** they can see compatibility but cannot queue deployment.

### Edge Cases

- Artifact references a type that no runtime integration currently supports.
- Artifact declares required capabilities that differ from artifact type defaults.
- An engine advertises a legacy short apply capability such as `workflow-definition.apply` but not the canonical artifact-type apply capability.
- Engine capability heartbeat is stale, missing, or comes from an engine in another environment.
- Engine advertises a broad capability but not the artifact schema version required by the artifact.
- Artifact payload reference has expired, points to a missing file, or cannot produce a download stream.
- Runtime downloads a payload whose digest does not match the command digest.
- Runtime applies locally but loses connectivity before reporting completion.
- Runtime applies one artifact successfully and then fails on a later artifact in the same command.
- Duplicate deploy clicks, duplicate commands, or duplicate runtime claims occur for the same revision and engine.
- Deployment command diagnostics contain raw workflow definitions, secrets, credentials, local paths, or connection strings.
- A revision contains more than one artifact type and only some are compatible with the selected engine.
- An archived artifact or archived engine is selected through a stale page or deep link.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST determine deployability for every artifact record in a desired-state revision against a selected target engine before deployment can be queued.
- **FR-002**: System MUST compare artifact type, artifact schema version, runtime kind or family constraints, required runtime capabilities, and target engine advertised capabilities during deployability checks.
- **FR-003**: System MUST treat artifact-declared compatibility hints as authoritative when present and artifact-type defaults as the fallback when hints are absent.
- **FR-003a**: System MUST use canonical artifact apply capability IDs in the form `artifact.{artifactTypeId}.apply`, such as `artifact.elsa.workflow-definition.apply`, when deriving artifact-type default apply requirements.
- **FR-004**: System MUST expose deployability results with status, blocking reasons, missing capabilities, unsupported artifact types, unsupported schema versions, stale engine metadata, and unavailable payload references.
- **FR-005**: System MUST prevent deployment to an engine outside the revision's target environment unless an explicit cross-environment promotion/deployment flow authorizes that target.
- **FR-006**: System MUST prevent deployment when any artifact in the revision is incompatible, unavailable, archived, fails required validation, or the target engine capability metadata is missing or stale.
- **FR-007**: System MUST keep engine capability data current through registration and heartbeat metadata, including supported artifact types, schema ranges, and runtime apply capabilities.
- **FR-008**: System MUST provide a safe artifact download path for console users with deployment read access and a separate runtime artifact download path authorized by the active command lease for the target engine and command.
- **FR-009**: System MUST never require users or runtimes to consume raw local filesystem paths as the deployment interface.
- **FR-010**: System MUST queue deployment runs only after permission checks, deployability checks, tier requirements, confirmation requirements, and idempotency checks pass.
- **FR-011**: System MUST create exactly one runtime deployment command for each approved revision, target engine, and deployment mode combination; the command MUST include all artifact records in that revision with safe artifact identity, revision identity, target engine, target environment, expected digest, artifact type, schema metadata, lease-scoped payload access instructions, per-artifact status, and idempotency key.
- **FR-012**: System MUST ensure runtime command payloads do not embed raw artifact content, workflow definitions, secrets, credentials, or provider tokens.
- **FR-013**: System MUST allow a runtime worker for the target engine to claim, download, validate, apply, and complete or fail the deployment command.
- **FR-014**: System MUST require the runtime to verify the downloaded artifact digest before applying it.
- **FR-015**: System MUST record runtime-reported progress, observed artifact digest, runtime reference, validation outcome, per-artifact apply outcomes, and safe diagnostics in deployment run history.
- **FR-016**: System MUST make deployment run history the authoritative user-facing status after a command has been queued.
- **FR-017**: System MUST present blocked deployment reasons in the console with actionable remediation paths, such as "install runtime applier", "refresh engine heartbeat", "register compatible engine", "restore artifact", or "fix artifact payload access"; stale or missing engine capability metadata MUST direct users to refresh or reconnect the target engine.
- **FR-018**: System MUST support multiple artifact records in one revision and require all required records to be deployable before enabling deployment.
- **FR-019**: System MUST preserve workspace isolation for artifact metadata, engine capability metadata, deployability checks, commands, downloads, and run history.
- **FR-020**: System MUST redact or avoid displaying local paths, raw workflow payloads, credentials, connection strings, secrets, and tokens in deployability diagnostics and run history.
- **FR-021**: System MUST make duplicate queue requests and duplicate runtime completions deterministic through idempotency and final-state handling.
- **FR-022**: System MUST allow deployability checks to run without mutating platform or runtime state.
- **FR-023**: System MUST mark a deployment run failed when runtime apply partially succeeds and then fails, MUST preserve per-artifact outcomes, and MUST require explicit operator recovery rather than claiming automatic rollback.
- **FR-024**: System MUST reject runtime artifact download attempts when the command lease is missing, expired, not owned by the requesting worker, or not associated with the requested artifact.

### Key Entities *(include if feature involves data)*

- **Deployability Result**: Evaluation of one desired-state revision against one target engine, including per-artifact status, missing capabilities, payload availability, and remediation guidance.
- **Artifact Apply Requirement**: The artifact type, schema, runtime kind or family, and runtime capabilities required to apply one artifact record.
- **Engine Apply Capability**: Runtime-advertised capability metadata describing which artifact types, schema versions, and apply operations the engine can perform; canonical apply capability IDs use the form `artifact.{artifactTypeId}.apply`.
- **Artifact Payload Access**: Safe download instruction or link that allows authorized console users and lease-authorized target runtimes to retrieve the artifact payload without exposing raw storage paths or provider credentials.
- **Deployment Command Payload**: Safe command metadata that tells a target runtime which revision and complete artifact record set to apply, what digest to verify for each artifact, each artifact's current apply status, and where to report status.
- **Runtime Apply Report**: Runtime-submitted progress or final outcome containing safe diagnostics, observed digest, runtime reference, per-artifact apply outcomes, and result status.
- **Deployment Blocker**: User-facing reason that prevents deployment, tied to a concrete remediation action when available.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A compatible workflow-definition artifact can be deployed from a desired-state revision to a compatible engine in an end-to-end test, with the run ending in succeeded state.
- **SC-002**: An engine missing `artifact.elsa.workflow-definition.apply` is blocked before queueing, and the UI names the missing canonical capability.
- **SC-003**: Users can identify why an artifact cannot deploy and the next remediation action from the revision detail page without reading logs.
- **SC-004**: Runtime command payloads and run history contain no raw artifact payloads, workflow definitions, secrets, credentials, provider tokens, connection strings, or unredacted local filesystem paths, and runtime artifact download attempts without a valid active command lease are rejected.
- **SC-005**: Digest mismatch, unsupported schema, stale or missing engine capabilities, and unavailable artifact payload are each covered by automated tests and produce distinct safe diagnostics; stale or missing engine capabilities block deployment before queueing.
- **SC-006**: Duplicate deploy requests for the same revision, target engine, and deployment mode do not create duplicate runtime apply side effects.
- **SC-007**: A runtime apply that succeeds for one artifact and fails for a later artifact marks the run failed, preserves per-artifact outcomes, and does not report automatic rollback.
- **SC-008**: Deployability evaluation for a revision with at least 10 artifact records and 10 candidate engines completes within 3 seconds in the integration test environment.

## Assumptions

- Existing artifact registry, desired-state revisions, deployment runs, runtime command sync, and runtime-side artifact applier concepts remain the foundation for this feature.
- The first concrete artifact type is `elsa.workflow-definition`; additional artifact types must use the same deployability model rather than special-case UI rules.
- Runtime engines advertise capabilities through registration and heartbeat metadata; stale or missing heartbeat data is treated as a blocker or warning according to deployment risk.
- Elsa Control owns compatibility, dispatch, audit, and history; runtime integrations own artifact interpretation and local apply semantics.
- Deployment download links are authorized platform actions, not direct exposure of storage paths.
- Direct push deployment remains out of scope unless a later transport-specific spec explicitly enables it.
- This feature does not replace promotion, rollback, runtime command sync, or runtime applier specs; it connects those capabilities into a complete deploy-to-engine path.
