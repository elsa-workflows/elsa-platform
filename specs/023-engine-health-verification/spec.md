# Feature Specification: Engine Health Verification

**Feature Branch**: `023-engine-health-verification`

**Created**: 2026-05-26

**Status**: Draft

**Input**: User description: "Proceed with the next deployment slice. New features must be specced with Spec Kit and fit within the deployment PRD and vision. Implement engine health verification so registered Elsa workflow engine endpoints can move from unreachable to verified health through explicit verification and heartbeat metadata, without expanding into live deployment apply or observability provider integrations."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify Registered Engine Health (Priority: P1)

A workspace member with deployment setup permission verifies a registered workflow engine from the Deployments console and sees health, version, certificate, credential verification, and last heartbeat metadata update from persisted workspace records.

**Why this priority**: Registered engines currently start as unreachable until an external process updates them. Verification is the smallest product slice that makes engine status understandable and unlocks safe runtime controls without pretending to perform live deployment apply.

**Independent Test**: Register an engine, run verification from the API or console, reload the cockpit, and verify that health, version, certificate status, credential verification status, last verification, and last heartbeat reflect the verification result without exposing raw credentials.

**Acceptance Scenarios**:

1. **Given** a workspace member with setup permission and a registered engine, **When** they request engine verification, **Then** the platform records a verification attempt and updates the engine health metadata from the verification result.
2. **Given** verification succeeds with trusted certificate and valid credential reference, **When** the cockpit is reloaded, **Then** the engine is shown as healthy with version, certificate, credential verification, last verified time, and last heartbeat time.
3. **Given** verification cannot reach the engine or cannot verify the credential reference, **When** the cockpit is reloaded, **Then** the engine remains unreachable or degraded with a safe diagnostic message and no raw credential values.

---

### User Story 2 - Accept Engine Heartbeats (Priority: P1)

A registered workflow engine or trusted runtime agent reports heartbeat metadata to the platform so the deployment cockpit can show recent reachability without a workspace operator manually verifying every engine.

**Why this priority**: Manual verification is useful during setup, but ongoing deployment safety depends on persisted heartbeat freshness. This also aligns with the existing runtime-control rule that unreachable engines must fail closed.

**Independent Test**: Send a heartbeat for a workspace-owned engine using an authorized caller, reload the cockpit, and verify health, version, certificate status, advertised capability metadata, and last heartbeat update only for that engine and workspace.

**Acceptance Scenarios**:

1. **Given** an authorized heartbeat caller for a registered engine, **When** it posts heartbeat metadata, **Then** the platform updates only that engine's health metadata and records heartbeat time.
2. **Given** a heartbeat references a missing or cross-workspace engine, **When** the request is submitted, **Then** the platform rejects it without updating any engine.
3. **Given** a heartbeat omits optional capability changes, **When** it is accepted, **Then** existing registered capability and control metadata is preserved.

---

### User Story 3 - Show Verification State In Console (Priority: P2)

A workspace member can see whether the selected engine is unverified, verifying, healthy, degraded, or unreachable, and can understand why controls are disabled.

**Why this priority**: The console must explain deployment safety state clearly; otherwise users will see disabled runtime controls without knowing how to resolve them.

**Independent Test**: Render engines in healthy, degraded, unreachable, and unverified states; verify the console shows the current state, last verification/heartbeat metadata, safe diagnostics, and appropriate action availability.

**Acceptance Scenarios**:

1. **Given** an engine has never been verified, **When** the user opens Engine Registration, **Then** the console shows a verification action and explains that controls remain disabled until reachability is verified.
2. **Given** an engine is healthy, **When** the user opens Engine Registration, **Then** supported runtime controls are available if permissions and capabilities also pass.
3. **Given** an engine is degraded or unreachable, **When** the user opens Engine Registration, **Then** controls are disabled and the console shows the latest safe diagnostic metadata.

### Edge Cases

- A workspace member lacks setup permission; they can read engine health but cannot trigger manual verification.
- A workspace member lacks runtime-control permission; verification may succeed, but controls remain disabled.
- The verification probe times out; the platform records the attempt and keeps the engine unreachable or degraded.
- A heartbeat arrives for an engine whose credential reference is expired or missing; health must not be promoted to healthy.
- A heartbeat tries to remove registered controls or add unsupported boundaries; the update is rejected or ignored according to the server contract.
- Repeated heartbeats arrive out of order; older metadata must not overwrite newer heartbeat timestamps.
- A direct API caller submits cross-workspace IDs; the system rejects the request without revealing whether the target engine exists.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow authorized workspace users to manually verify a registered workflow engine's reachability from the deployment API and console.
- **FR-002**: System MUST persist each engine's latest verification result, including health, version when available, certificate status, credential verification status, last verified time, last heartbeat time, and a safe diagnostic message.
- **FR-003**: System MUST never persist or return raw engine API credentials, provider tokens, or secret values as part of verification or heartbeat responses.
- **FR-004**: System MUST reject manual verification requests from users who are not workspace members or lack deployment setup permission.
- **FR-005**: System MUST accept heartbeat metadata only from an authorized caller scoped to the target workspace and engine.
- **FR-006**: System MUST reject heartbeat or verification updates for missing, cross-workspace, or mismatched environment/engine records.
- **FR-007**: System MUST treat successful reachability plus trusted certificate plus verified credential reference as healthy engine state.
- **FR-008**: System MUST treat failed reachability as unreachable and failed credential or certificate checks as degraded unless reachability also failed.
- **FR-009**: System MUST preserve existing registered capabilities and controls when a heartbeat does not explicitly include capability metadata.
- **FR-010**: System MUST prevent stale heartbeat metadata from overwriting newer heartbeat metadata.
- **FR-011**: System MUST expose engine verification state and safe diagnostics through the deployment cockpit.
- **FR-012**: Console users MUST be able to trigger manual verification, see pending/success/failure states, and refresh cockpit data after verification.
- **FR-013**: Runtime controls MUST remain blocked when engine health is unreachable and MUST become eligible only when permission, capability, confirmation, and verified health gates pass.
- **FR-014**: Verification and heartbeat behavior MUST remain within persisted deployment metadata scope; live deployment apply, runtime instance inspection, live drift detection, and telemetry provider calls are out of scope.
- **FR-015**: Verification attempts and heartbeat updates MUST be auditable through persisted metadata sufficient to diagnose current cockpit state.

### Key Entities *(include if feature involves data)*

- **Engine Verification Result**: Latest verification outcome for a workflow engine, including health, version, certificate status, credential verification status, timestamps, and safe diagnostic message.
- **Engine Heartbeat**: Runtime-originated metadata update that reports recent reachability, version, certificate, credential verification, optional capabilities, and heartbeat time.
- **Workflow Engine Registration**: Existing workspace-owned engine record that receives verification and heartbeat metadata.
- **Verification Actor**: Workspace user or trusted runtime caller that submitted the verification or heartbeat update.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workspace member with setup permission can verify a registered engine from the console and see cockpit health metadata refresh without a full browser reload.
- **SC-002**: API tests prove manual verification and heartbeat updates cannot mutate engines outside the caller's workspace.
- **SC-003**: Automated tests prove raw credential values and provider tokens are absent from verification, heartbeat, cockpit, and console outputs.
- **SC-004**: A successful verification moves a newly registered engine from unreachable to healthy when reachability, certificate, and credential checks pass.
- **SC-005**: Failed reachability, certificate, and credential checks produce distinct safe degraded/unreachable states visible in the cockpit.
- **SC-006**: Runtime controls remain unavailable for unreachable engines and become available for healthy engines when permissions and capabilities are present.
- **SC-007**: Heartbeat processing rejects stale updates and preserves newer heartbeat metadata.
- **SC-008**: Focused backend and console tests for verification and heartbeat behavior pass before the feature is considered complete.

## Assumptions

- Existing deployment workspace identity, permissions, cockpit projection, and runtime-control gating from `specs/022-deployment-ux` remain the foundation.
- The first implementation may use a deterministic verification/probe adapter suitable for local and automated tests; production engine-specific probe details can be extended later behind the same contract.
- Heartbeat authentication can initially reuse trusted platform/workspace API authorization patterns available in the repository; long-lived runtime-issued heartbeat credentials are a future hardening slice unless already available locally.
- Live deployment apply, live observability provider queries, live drift detection, runtime instance state inspection, and provider-specific cloud health checks are out of scope.
- The outer tenant boundary remains the workspace; future runtime tenant overlays do not replace workspace ownership.
