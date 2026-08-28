# Feature Specification: Deployment UX

**Feature Branch**: `022-deployment-ux`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "Implement workspace deployment functionality and hook it up to the console UX: durable workflow applications, environments, engine registrations, desired-state revision comparison, validation, deployment run tracking, rollback, capability-gated runtime controls, and replacement of the in-memory cockpit with workspace-authorized APIs."

## Clarifications

### Session 2026-05-25

- Q: Which role model should govern deployment setup, deploy, rollback, and runtime controls? -> A: Flexible roles and permissions system.
- Q: How should deployment runs execute in the first slice? -> A: Durable queued runs with in-process worker.
- Q: What is the first desired-state storage model? -> A: Structured platform records first.
- Q: What observability and drift behavior is included in this slice? -> A: Persisted metadata and manual status only.
- Q: What approval or confirmation is required for risky actions? -> A: Explicit single-user confirmation.
- Q: How should future runtime integrations receive deployment work? -> A: Elsa Control-owned durable deployment commands with transport-independent delivery; runtime pull/sync is the preferred default, with webhook-triggered fetch and direct push as optional transports.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Real Deployment Cockpit (Priority: P1)

A signed-in workspace member opens Deployments and sees workflow applications, environments, engine registrations, desired revisions, deployment status, drift, and history loaded from workspace-owned records rather than sample data.

**Why this priority**: This replaces the current demo cockpit with a trustworthy workspace control-plane view and gives every later action a durable workspace context.

**Independent Test**: Seed one workspace with applications, environments, engines, desired revisions, and deployment history, then sign in as a member and verify the cockpit shows only that workspace's data. Sign in as a non-member and verify access is denied.

**Acceptance Scenarios**:

1. **Given** a workspace member with deployment records, **When** they open Deployments, **Then** the cockpit lists the workspace's applications, environments, engine health, desired revision, deployed revision, drift status, and recent deployment history.
2. **Given** two workspaces with different deployment records, **When** a member of one workspace requests the other workspace's deployment cockpit, **Then** the system denies access and exposes no cross-workspace data.
3. **Given** a workspace with no deployment records, **When** a member opens Deployments, **Then** the console shows an empty state that can start application and environment setup.

---

### User Story 2 - Register Workflow Engines (Priority: P1)

A workspace member with deployment setup permission registers workflow applications, environments, and Elsa workflow engines with endpoint metadata, credential references, advertised capabilities, and optional hosting provider metadata.

**Why this priority**: Deployment management needs concrete registered engines before promotion, validation, runtime controls, observability, or deployment execution can be meaningful.

**Independent Test**: Register a workflow application, an environment, and an engine with a secret reference and capabilities, then reload the cockpit and verify the engine is shown with supported controls and no raw credentials.

**Acceptance Scenarios**:

1. **Given** a workspace member with the required deployment setup permission and deployment entitlement, **When** they create an application, environment, and engine registration, **Then** the platform stores those records as workspace-owned deployment data.
2. **Given** an engine registration includes a credential reference, **When** the cockpit returns engine details, **Then** it includes the reference metadata and verification status but never includes the raw credential value.
3. **Given** a workspace member without the required deployment setup permission attempts to register or modify an engine, **When** the request is submitted, **Then** the system rejects the mutation based on current workspace permissions and entitlement.

---

### User Story 3 - Preview Promotion And Validation (Priority: P2)

A workspace member with promotion preview permission compares desired-state revisions across environments before deployment and sees differences, validation results, blockers, warnings, and rollback candidates.

**Why this priority**: Users need a safe review step before changing any target engine state, especially for production environments.

**Independent Test**: Create source and target environment revisions with changed workflow, feature, runtime, secret, observability, and engine-binding data, then compare them and verify the diff and validation blockers are returned without mutating the target.

**Acceptance Scenarios**:

1. **Given** source and target environments have different desired revisions, **When** a workspace member with promotion preview permission opens promotion preview, **Then** the system shows categorized differences for workflow, feature, shell, runtime, secret reference, observability, and engine-binding changes.
2. **Given** a target environment is missing a required secret reference, engine capability, entitlement, or reachable engine, **When** a workspace member with promotion preview permission previews deployment, **Then** validation returns blockers and the deploy action is unavailable.
3. **Given** validation has only passes or warnings, **When** a workspace member with deployment execution permission reviews the preview, **Then** the console exposes a dry-run or deploy path with the warnings visible.

---

### User Story 4 - Track Deployment Runs And Rollback (Priority: P2)

A workspace member with deployment execution permission starts a deployment run, follows status, sees immutable history, and can roll back to a compatible previously deployed revision.

**Why this priority**: Deployment functionality is incomplete until users can execute, audit, and recover from changes.

**Independent Test**: Start a deployment from a valid preview, verify a run is recorded with actor, target environment, engine, validation outcome, status, command metadata, and deployed revision, then roll back to a previous compatible revision and verify history records both events.

**Acceptance Scenarios**:

1. **Given** a deployment preview has no blockers, **When** a member with deployment execution permission starts deployment, **Then** the system records a deployment run with actor, source revision, target environment, target engine, validation result, status, and a durable command record or queued-work equivalent.
2. **Given** a deployment run is in progress or completed, **When** the user opens history, **Then** the cockpit shows immutable run events and resulting environment revision state.
3. **Given** a previously deployed revision is still compatible with the target environment and engine capabilities, **When** a member with rollback permission chooses rollback, **Then** the system creates a new deployment run that redeploys that known-good revision and records rollback source metadata.

---

### User Story 5 - Use Capability-Gated Runtime Controls (Priority: P3)

A workspace member sees only the runtime controls that the selected engine or hosting adapter explicitly supports, and every control execution is authorized and audited.

**Why this priority**: Runtime controls are useful but risky; the platform must distinguish workflow, engine API, shell, and hosting boundaries instead of presenting vague generic actions.

**Independent Test**: Register engines with different capability sets, open each engine's operations view, verify unsupported controls are hidden or disabled, and verify direct requests for unsupported or unauthorized controls are rejected.

**Acceptance Scenarios**:

1. **Given** an engine advertises pause-processing and reload-configuration capabilities, **When** the user opens operations, **Then** only those matching controls are available.
2. **Given** no hosting provider adapter is configured, **When** the user opens operations, **Then** host infrastructure controls are unavailable.
3. **Given** a user attempts a supported control without the required permission grant or entitlement, **When** the request is submitted, **Then** the system rejects the request and records no runtime action.

---

### User Story 6 - Replace Demo UX With Working Console Flows (Priority: P3)

The console Deployments page uses live workspace APIs for loading, creation, preview, execution, rollback, and operations while preserving clear loading, empty, error, and authorization states.

**Why this priority**: The existing UI already frames the desired experience, but it must become a real workspace workflow rather than a read-only sample cockpit.

**Independent Test**: Use the console to create deployment setup records, preview a promotion, start a deployment run, inspect history, and verify refresh/error states without manually calling APIs.

**Acceptance Scenarios**:

1. **Given** the workspace has no deployment setup, **When** a member opens Deployments, **Then** the console guides them through creating a workflow application, environment, and engine registration.
2. **Given** deployment APIs return loading, empty, validation-blocked, unauthorized, or unexpected states, **When** the console renders those responses, **Then** controls and messaging match the actual state and do not show fake sample values.
3. **Given** a mutation succeeds, **When** the user returns to the cockpit, **Then** the relevant application, environment, engine, deployment run, or history section refreshes without requiring a full page reload.

### Edge Cases

- A workspace has no deployment entitlement; reads remain available where allowed, but privileged mutations and deploy actions are blocked.
- A registered engine is unreachable, has an untrusted certificate, or cannot verify credentials; validation fails closed before deployment or runtime controls mutate state.
- A desired-state revision references a secret, capability, package, feature, or observability binding that no longer exists or is no longer visible to the workspace.
- Two users attempt to deploy to the same environment concurrently; only one active deployment for the target environment can proceed.
- A rollback target is missing required secrets or engine capabilities; rollback is blocked before changing the target engine state.
- A user refreshes the console while a deployment is running; the latest run status and history remain visible.
- The API process restarts after a deployment run is queued or claimed; queued runs remain eligible for processing, stale claimed runs are marked `RecoveryRequired` with append-only history, and no automatic duplicate apply occurs without an explicit new confirmation.
- A future runtime sync worker polls or receives duplicate webhook notifications for the same deployment command; command idempotency prevents duplicate apply attempts.
- A future target runtime is reachable only by outbound network access; the runtime pull/sync transport remains compatible with deployment run tracking.
- A direct API caller submits unsupported runtime control IDs or cross-workspace IDs; the system rejects the request even if the console would not show that action.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST persist workspace-owned workflow applications, environments, workflow engine registrations, desired-state revisions, deployment validations, deployment runs, deployment history, drift reports, observability bindings, and runtime control audit metadata.
- **FR-002**: System MUST replace seeded deployment cockpit data with workspace-owned records returned through authorized workspace APIs.
- **FR-003**: System MUST enforce current workspace membership, flexible role/permission grants, entitlement, and operator/customer separation rules for every deployment read and mutation.
- **FR-004**: System MUST allow authorized users to create and update workflow applications and environments within a workspace.
- **FR-005**: System MUST allow authorized users to register workflow engines with endpoint metadata, credential references, health status, capability sets, and optional hosting provider metadata.
- **FR-006**: System MUST never expose raw engine API credentials, provider tokens, or secret values through customer-facing responses, console state, workflow artifacts, desired-state revisions, or audit records.
- **FR-007**: System MUST expose engine controls only when supported by the engine capability set or an explicitly configured hosting provider adapter.
- **FR-008**: System MUST distinguish workflow, engine API, shell, and hosting control boundaries in API responses, console labels, validation, and audit records.
- **FR-009**: System MUST allow authorized users to compare desired-state revisions across environments before deployment.
- **FR-009a**: System MUST store desired-state revisions as structured platform records for workflows, features, shell configuration, runtime configuration, secret references, observability bindings, and engine bindings; manifest/artifact import and export are deferred from the first slice.
- **FR-010**: System MUST validate required secret references, engine reachability, certificate trust, engine capabilities, workspace entitlements, effective workspace permissions, and active deployment conflicts before deployment or rollback.
- **FR-011**: System MUST block deployment, rollback, and runtime control execution when validation has blockers.
- **FR-011a**: System MUST require explicit single-user confirmation before executing deployment, rollback, or runtime control actions; full multi-party approval workflows are deferred from the first slice.
- **FR-012**: System MUST record deployment attempts, validation failures, successful deployments, failed deployments, cancellations where supported, rollback attempts, actor identity, target environment, target engine, source revision, deployed revision, and resulting status.
- **FR-013**: System MUST support rollback by creating a new deployment run from a previously recorded compatible desired-state revision.
- **FR-014**: System MUST persist and display drift status and drift report metadata without silently overwriting desired or observed state; live drift detection against engines is deferred from the first slice.
- **FR-015**: System MUST expose cockpit summary data for applications, environments, engines, promotion comparisons, validation results, observability binding metadata, deployment history, drift report metadata, and available controls.
- **FR-016**: Console users MUST be able to perform setup, preview, deployment, rollback, and supported runtime controls through live workspace APIs without relying on sample data.
- **FR-017**: Console UX MUST include clear empty, loading, unauthorized, validation-blocked, running, succeeded, failed, and unexpected states.
- **FR-018**: System MUST keep all deployment state scoped under workspace ownership while leaving future runtime tenant or deployment tenant overlays nested under that workspace boundary.
- **FR-019**: System MUST support flexible workspace permission grants that can be composed into roles for deployment actions, including separate permissions for deployment setup, desired-state management, promotion preview, deployment execution, rollback, runtime controls, observability management, and read-only access.
- **FR-020**: System MUST execute deployment and rollback runs as durable queued work, with the first implementation allowed to use an in-process worker that records queued, running, completed, failed, and `RecoveryRequired` outcomes in persistent run history; on startup, queued runs are processed normally and stale running runs are moved to `RecoveryRequired` rather than replayed automatically.
- **FR-021**: System MUST keep deployment run execution compatible with a future durable deployment command contract that supports runtime pull/sync, webhook-triggered fetch, and direct push transports without changing the console-facing run/history model.
- **FR-022**: System MUST treat webhook notifications as non-authoritative triggers; authoritative deployment state remains the persisted deployment run and command record.

### Key Entities *(include if feature involves data)*

- **Workflow Application**: Workspace-owned grouping for related deployment environments, engines, desired state, observability, and history.
- **Deployment Environment**: Named deployment context such as dev, test, stage, or production within a workflow application.
- **Workflow Engine Registration**: A concrete Elsa workflows application instance attached to an environment with endpoint metadata, credential reference, health, capabilities, and optional hosting provider metadata.
- **Desired-State Revision**: Immutable versioned snapshot composed from structured platform records for deployable workflow, feature, shell, runtime, secret-reference, observability, and engine-binding intent.
- **Promotion Comparison**: Reviewable diff and validation result between source and target environment revisions.
- **Deployment Run**: Auditable attempt to validate, deploy, or roll back a desired-state revision to a target environment and engine.
- **Deployment Command**: Durable work item or queued-work equivalent linked to a deployment run, carrying target engine, desired revision or artifact reference, action, idempotency, status, and safe diagnostics for runtime integration.
- **Runtime Control Action**: Capability-gated operation against workflow processing, engine API, shell, or hosting infrastructure.
- **Observability Binding**: Workspace-owned connection metadata for logs, console streams, traces, or metrics related to environments, engines, workflows, instances, or deployment revisions.
- **Drift Report**: Persisted metadata describing a known difference between desired state and current engine state with review, redeploy, or import guidance.
- **Workspace Permission Grant**: Server-authoritative permission assignment that determines which deployment actions a workspace member may perform independently of frontend state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workspace member can open Deployments and see persisted cockpit data for their workspace in under 3 seconds with seeded test data.
- **SC-002**: Cross-workspace API and console tests prove members cannot view or mutate deployment records outside their workspace.
- **SC-003**: A workspace member with deployment setup permission can create an application, environment, and engine registration through the console without direct API calls.
- **SC-004**: A workspace member with promotion preview permission can preview an environment promotion and identify all blockers before any target engine state is changed.
- **SC-005**: A valid deployment run records actor, target, validation outcome, status, and history, and is visible after page refresh.
- **SC-006**: A rollback to a compatible previous revision creates a new auditable run and updates environment deployed revision state.
- **SC-007**: Unsupported runtime controls are absent or disabled in the console and rejected by direct API requests.
- **SC-008**: Customer-facing responses and console state contain zero raw credential or secret values in automated tests.
- **SC-009**: Observability and drift views render persisted metadata without requiring live telemetry provider credentials or network calls.
- **SC-010**: Deploy, rollback, and runtime control tests prove actions cannot execute until the initiating user provides explicit confirmation.
- **SC-011**: Deployment run persistence can represent queued, claimed/running, completed, failed, and recovery-required command states without duplicate apply after process restart or repeated delivery.

## Assumptions

- Existing customer identity and workspace authorization from `specs/021-identity-tenancy` are the authority for this feature.
- Existing deployment abstractions and engine packages are reused for validation, diff, dry-run, apply, and history where they already fit; manifest parsing and artifact packaging remain available subsystem foundations but are not required for the first desired-state UX.
- The first implementation may use local/fake engine and control adapters for testable apply behavior; production cloud/provider adapters can follow as separate provider-specific features.
- Desired-state authoring starts with platform-owned structured records and test fixtures; manifest/artifact import/export, full GitOps, OCI promotion, signatures, and external approval workflows are out of scope for this slice.
- The first slice can execute queued runs with an in-process worker, but the data model and history should remain compatible with later runtime-side sync workers that claim platform-owned deployment commands.
- Webhooks are optional notification accelerators, not the source of deployment authority; runtimes should fetch the command from the platform before acting.
- Full multi-party approval workflows are out of scope for this slice; explicit confirmation by the initiating authorized user is required for risky actions.
- Runtime tenant overlays remain future nested concerns and do not replace workspace ownership.
