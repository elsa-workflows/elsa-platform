# Feature Specification: Deployment Foundation Contracts

**Feature Branch**: `017-deployment-contracts`

**Created**: 2026-05-20

**Status**: Draft

**Input**: User description: "Add the next Phase 1 slice for the Elsa Deployment Platform by establishing the deployment foundation contracts and project skeletons that support manifest -> artifact -> validation -> dry-run -> apply -> history without implementing the full reconciliation engine yet."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Describe Deployable Resources (Priority: P1)

A platform maintainer can model the smallest stable deployment language for resources, targets, artifacts, plans, changes, results, and history so later manifest, artifact, engine, API, and CLI work can share one vocabulary.

**Why this priority**: Every later deployment package depends on these names and boundaries. The contracts must exist before manifest parsing, artifact IO, or reconciliation code can converge on a common shape.

**Independent Test**: Can be tested by creating representative resource, artifact, plan, change, result, and history records and verifying their required identity, status, diagnostics, and digest data are present without referencing runtime state.

**Acceptance Scenarios**:

1. **Given** a workflow resource and a variable resource, **When** each is represented as desired deployment state, **Then** each has a stable resource type, logical id, optional scope, dependencies, and content hash.
2. **Given** a deployment plan containing create, update, no-op, unsupported, and conflict changes, **When** the plan is inspected, **Then** each change can be traced back to the desired resource and target state comparison.
3. **Given** a completed or partially failed deployment result, **When** history is recorded, **Then** the artifact identity, target identity, actor, per-resource outcomes, diagnostics, and timestamps are represented.

---

### User Story 2 - Enforce Platform Boundaries (Priority: P2)

A platform contributor can add the deployment foundation packages to the solution without coupling deployment abstractions to Package Catalog implementation, Runtime Builder implementation, API hosting, persistence, or runtime execution state.

**Why this priority**: The deployment platform must stay transport-agnostic, environment-agnostic, and control-plane-only. Boundary tests catch architectural drift while the system is still small.

**Independent Test**: Can be tested by inspecting project references and proving the foundation package has no references to catalog API/UI/persistence, runtime builder core, hosting packages, or runtime-state concepts.

**Acceptance Scenarios**:

1. **Given** the deployment foundation package, **When** its dependencies are inspected, **Then** it only depends on base runtime libraries and approved catalog abstractions when package validation descriptors require them.
2. **Given** the deployment foundation contracts, **When** they are searched for excluded runtime-state concepts, **Then** they do not model workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.

---

### User Story 3 - Prepare Extension Points (Priority: P3)

A third-party platform extension author can see where future resource handlers, validators, artifact readers, artifact writers, targets, and history stores will plug in without those extension points committing to a specific transport or host.

**Why this priority**: Extensibility must exist early enough to avoid a closed engine design, but only the minimum required extension contracts should be introduced in this slice.

**Independent Test**: Can be tested by defining placeholder extension implementations in tests for a sample resource handler, artifact reader, artifact writer, target, validator, and history store.

**Acceptance Scenarios**:

1. **Given** a sample resource handler, **When** it declares the resource type it supports, **Then** the contract can express read, validate, diff, dry-run, and apply responsibilities without requiring API hosting or persistence.
2. **Given** a sample artifact reader and writer, **When** they expose artifact metadata and content access, **Then** the contracts do not assume folder, ZIP, OCI, NuGet, or any single transport format.

### Edge Cases

- Resource identities must reject empty resource types and empty logical ids.
- Deployment status must distinguish success, no-op, validation failure, partial application, failed application, and completed-with-warnings outcomes.
- Diagnostics must support machine-readable codes, severity, human-readable messages, and resource association when applicable.
- Partial failures must be representable per resource without implying automatic transaction rollback.
- Deletion behavior must be explicit and conservative; absence from a manifest does not imply deletion.
- Unknown resource types must be representable as unsupported or unhandled rather than silently ignored.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define a stable deployment resource identity model covering resource type, logical id, optional scope, optional version, dependencies, desired state hash, and deletion behavior.
- **FR-002**: The system MUST define artifact identity and metadata covering artifact id, version, manifest digest, content digest, build timestamp, builder metadata, source metadata, and schema version.
- **FR-003**: The system MUST define deployment plan and change models that can represent create, update, activate, deactivate, delete, no-op, unsupported, and conflict changes.
- **FR-004**: The system MUST define deployment result and operation result models that can represent validation failure, dry-run success, apply success, partial application, failed application, retryability, diagnostics, and per-resource outcomes.
- **FR-005**: The system MUST define a deployment history record that captures deployment id, artifact identity, manifest identity, target identity, actor, status, plan snapshot, resource outcomes, diagnostics, and timestamps.
- **FR-006**: The system MUST define minimal extension contracts for resource handlers, target state readers, validators, artifact readers, artifact writers, deployment engine entry points, deployment targets, and history stores.
- **FR-007**: The system MUST preserve strict control-plane/data-plane separation by excluding workflow instances, bookmarks, execution state, logs, locks, queues, and transient runtime state from deployment foundation contracts.
- **FR-008**: The system MUST expose boundary tests proving deployment foundation packages do not reference Package Catalog implementation, Runtime Builder implementation, API hosting, persistence, migration, UI, or runtime-state packages.
- **FR-009**: The system MUST provide contract tests covering identity validation, status taxonomy, diagnostic representation, plan/change/result composition, and sample extension implementations.
- **FR-010**: The system MUST update the solution and repository documentation so maintainers can locate the deployment foundation package and understand the deferred Phase 1 capabilities.

### Key Entities *(include if feature involves data)*

- **Deployment Resource**: Desired control-plane state for a workflow definition, variable, descriptor, or future resource type.
- **Deployment Artifact**: Immutable package of manifest content and resource files that can be validated, inspected, diffed, dry-run, applied, and recorded.
- **Deployment Target**: Named destination context that handlers use to read and apply control-plane state.
- **Deployment Plan**: Deterministic set of changes derived from comparing desired resources to target state.
- **Deployment Change**: Resource-specific action proposed by a plan.
- **Deployment Result**: Outcome of validation, dry-run, or apply execution.
- **Deployment History Record**: Audit record for a deployment attempt and its per-resource outcomes.
- **Deployment Diagnostic**: Structured message emitted during validation, planning, dry-run, apply, or history recording.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Maintainers can add a sample workflow resource and variable resource to a plan and assert all required identity, diff, result, and history fields through automated tests.
- **SC-002**: Automated dependency boundary tests verify the deployment foundation package has zero references to catalog API/UI/persistence, runtime builder implementation, hosting, migration, or runtime-state packages.
- **SC-003**: The full solution test suite passes with the new deployment foundation package and tests included.
- **SC-004**: The deployment foundation contracts support at least one sample implementation each for a resource handler, artifact reader, artifact writer, validator, target, and history store in tests.
- **SC-005**: Documentation identifies which Phase 1 capabilities are enabled by this slice and which capabilities remain deferred to manifest parsing, artifact IO, engine, API, and CLI slices.

## Assumptions

- This slice creates contracts, skeleton packages, tests, and documentation only; it does not implement manifest parsing, artifact ZIP/folder IO, reconciliation execution, CLI commands, hosted API endpoints, or Elsa runtime adapters.
- Workflow definitions and variables remain the first fully reconciled Phase 1 resource types, but this slice only models the generic deployment resource shape.
- Package descriptor validation continues to use Package Catalog abstractions and must not depend on Package Catalog implementation packages.
- Reapply-based resume and previous-artifact rollback semantics are documented in the roadmap but are not fully implemented in this slice.
- Any implementation names introduced here should remain narrow enough to support Phase 1 while leaving OCI, signing, operators, overlays, policies, Kubernetes CRDs, and multi-tenant reconciliation deferred.
