# Feature Specification: Deployment Engine MVP

**Feature Branch**: `020-deployment-engine`

**Created**: 2026-05-20

**Status**: Draft

**Input**: User description: "Implement the Phase 1 deployment engine MVP for Elsa Control. The engine must consume normalized manifests and inspected artifacts, validate desired resources, produce dry-run plans, apply idempotent resource changes through resource handlers, and record deployment history. It must remain transport-, hosting-, and environment-agnostic, avoid workflow runtime state, and defer CLI/API/persistence/operator features to later slices."

## Clarifications

### Session 2026-05-20

- Q: What should Phase 1 rollback mean? -> A: Record partial failure and resumable history; no cross-resource transaction rollback.
- Q: How should delete/prune behavior work in Phase 1? -> A: Deletion is opt-in per request and represented as planned delete changes.
- Q: Which resource handlers are required for the first engine implementation? -> A: Engine contract and in-memory test handlers first; product-specific handlers can be added incrementally.
- Q: How should history be stored in this slice? -> A: Through an abstraction plus in-memory implementation only; durable persistence is deferred.
- Q: Does dry-run record deployment history? -> A: No; dry-run returns a plan and diagnostics without mutating resources or history.
- Q: What contract gaps must be closed before engine implementation? -> A: Add minimal abstraction support for reading normalized deployment resources from an artifact and passing per-run execution context containing actor and prune preference.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate Desired Deployment (Priority: P1)

A deployment tool can submit a desired manifest or inspected artifact to the engine and receive structured validation results before any environment changes are planned or applied.

**Why this priority**: Validation is the first engine boundary after manifest/artifact work and prevents invalid desired state from reaching dry-run or apply flows.

**Independent Test**: Can be tested with in-memory resource validators and sample manifest resources, verifying that valid resources continue and invalid resources produce diagnostics without handler apply calls.

**Acceptance Scenarios**:

1. **Given** a desired state containing supported workflow and recipe resources, **When** validation runs, **Then** the engine returns success diagnostics and keeps the normalized resource identities.
2. **Given** a desired state containing an unsupported resource type, **When** validation runs, **Then** the engine returns a resource-scoped diagnostic and does not produce an applyable plan.
3. **Given** a resource validator reports an error, **When** validation completes, **Then** the result is unsuccessful and includes that validator diagnostic.

---

### User Story 2 - Produce Dry-Run Plan (Priority: P2)

A deployment tool can ask the engine to compare desired resources against target state and receive a deterministic dry-run plan that describes create, update, delete, and no-op changes without applying them.

**Why this priority**: Dry-run proves the core deployment loop after validation and gives CLI/API slices a stable preview contract.

**Independent Test**: Can be tested with in-memory state readers and resource handlers, verifying planned actions for missing, matching, changed, and extra managed resources without mutating state.

**Acceptance Scenarios**:

1. **Given** a desired resource that does not exist in target state, **When** dry-run executes, **Then** the plan includes a create change.
2. **Given** a desired resource whose desired-state hash differs from target state, **When** dry-run executes, **Then** the plan includes an update change.
3. **Given** a desired resource whose desired-state hash matches target state, **When** dry-run executes, **Then** the plan includes a no-op change.
4. **Given** target state contains a managed resource absent from desired state and pruning is enabled, **When** dry-run executes, **Then** the plan includes a delete change.

---

### User Story 3 - Apply Plan And Record History (Priority: P3)

A deployment tool can apply a validated plan through resource handlers and receive deployment history records that summarize the run, per-resource results, actor, target, mode, artifact identity, and diagnostics.

**Why this priority**: Apply plus history completes the Phase 1 loop: manifest -> artifact -> validation -> dry-run -> apply -> history.

**Independent Test**: Can be tested with in-memory handlers and history store by applying a plan and verifying handler invocation order, idempotent results, partial failure diagnostics, and recorded history.

**Acceptance Scenarios**:

1. **Given** a plan with create and update changes, **When** apply executes, **Then** only applyable changes invoke matching resource handlers.
2. **Given** a handler reports a resource failure, **When** apply completes, **Then** the deployment result is partial or failed and history records the failed resource with diagnostics.
3. **Given** apply completes successfully, **When** history is recorded, **Then** the history record includes deployment ID, target, actor, mode, artifact identity if present, resource results, and timestamps.

### Edge Cases

- Missing handler for a desired resource type must produce diagnostics and prevent apply for that resource.
- Invalid artifact inspection results must prevent validation, dry-run, and apply.
- Duplicate resource identities must be rejected before planning.
- Handler validation failure must not call apply.
- Dry-run must not mutate target state or history.
- Apply must skip no-op changes unless the handler explicitly supports a verify operation.
- Partial apply failures must be represented in the result and history without pretending rollback occurred.
- Empty desired state must be valid for validation and dry-run, but apply should only delete managed resources when pruning is explicitly enabled.
- The engine must not read, reconcile, or mutate workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a deployment engine service that can validate desired deployment resources from normalized manifests or inspected artifacts.
- **FR-002**: The system MUST route each resource to a matching resource validator and resource handler by deployable resource type.
- **FR-003**: The system MUST reject duplicate resource identities in one desired deployment.
- **FR-004**: The system MUST produce structured diagnostics for unsupported resource types, invalid resources, duplicate identities, invalid artifacts, and handler failures.
- **FR-005**: The system MUST compare desired resource hashes against target state to produce deterministic create, update, delete, and no-op changes.
- **FR-006**: The system MUST support dry-run mode that returns a plan without mutating resource state or recording apply history.
- **FR-007**: The system MUST support apply mode that invokes handlers only for applyable changes.
- **FR-008**: The system MUST record deployment history through an abstraction supplied by the caller, with an in-memory implementation available for Phase 1 tests and samples.
- **FR-009**: The system MUST include deployment ID, target descriptor, actor, operation mode, artifact identity when available, timestamps, per-resource results, and diagnostics in history.
- **FR-010**: The system MUST represent partial failures explicitly and must not claim transactional rollback unless a handler reports its own compensating action.
- **FR-011**: The system MUST keep engine contracts transport-, hosting-, persistence-, and environment-agnostic.
- **FR-012**: The system MUST preserve control-plane/data-plane separation by operating only on manifest-declared deployable resource state, not runtime execution state.
- **FR-013**: The system MUST allow third-party deployable resource types to participate through validator, state reader, and handler extension points.
- **FR-014**: The system MUST defer CLI commands, HTTP APIs, persistent history stores, approvals, signatures, GitOps, Kubernetes CRDs, distributed reconciliation, policy engines, and multi-tenant operators to later slices.
- **FR-015**: The system MUST use the existing deployment abstractions diagnostic, plan, resource, target, artifact, result, and history concepts unless implementation feedback proves a contract gap.
- **FR-016**: The system MUST expose normalized deployment resources through the artifact abstraction without requiring the engine to depend on concrete manifest or artifact packages.
- **FR-017**: The system MUST carry per-run execution context, including optional actor and prune preference, through engine operations without coupling the engine to CLI, API, or hosting concepts.

### Key Entities *(include if feature involves data)*

- **Deployment Request**: Desired deployment input with target descriptor, actor, operation mode, resources, optional artifact identity, and pruning preference.
- **Deployment Validation Result**: Structured outcome containing resource diagnostics and whether planning may proceed.
- **Deployment Plan**: Ordered collection of proposed changes with action, status, before/after resource state, and diagnostics.
- **Deployment Change**: One create, update, delete, or no-op operation for a resource identity.
- **Resource Handler**: Extension point that validates, plans, and applies one deployable resource type.
- **Resource State Reader**: Extension point that reads current managed state for one target and resource type.
- **Deployment Result**: Outcome of dry-run or apply with plan, resource results, status, diagnostics, and optional history record ID.
- **Deployment History Record**: Durable or in-memory audit entry representing one engine run and its resource outcomes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A valid two-resource desired state can be validated, dry-run, applied, and recorded in history in one automated test flow.
- **SC-002**: Dry-run produces create, update, no-op, and delete changes in deterministic resource identity order across repeated runs.
- **SC-003**: Apply invokes handlers only for create, update, and delete changes and never for no-op changes in Phase 1 tests.
- **SC-004**: Unsupported resource types and duplicate resource identities produce diagnostics and prevent successful apply in 100% of covered tests.
- **SC-005**: A handler failure produces a partial or failed deployment result and a history record containing the failed resource diagnostic.
- **SC-006**: Boundary tests prove the engine package has no CLI, API, hosting, Kubernetes, OCI, signing, policy, or runtime-state dependencies.

## Assumptions

- Phase 1 engine implementation starts with in-memory extension points and history storage; persistent storage is a later slice.
- Workflow, recipe, package, feature, and variable handlers can be introduced incrementally; the first implementation may use test handlers to prove the engine contract before product-specific handlers exist.
- Artifact inspection is a prerequisite for artifact-based engine requests; the engine rejects invalid inspection results rather than repairing artifacts.
- Pruning/deletion is opt-in for Phase 1.
- Rollback means "record failure and leave resumable history" in Phase 1, not transaction rollback across heterogeneous resource handlers.
