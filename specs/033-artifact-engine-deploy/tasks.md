# Tasks: Artifact To Engine Deployment

**Input**: Design documents from `/specs/033-artifact-engine-deploy/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included because the feature specification requires automated coverage for compatibility, stale capability metadata, unavailable payloads, digest mismatch, partial apply, duplicate queueing, and end-to-end deployment behavior.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify generated artifacts and existing project wiring before implementation.

- [ ] T001 Verify current feature branch and Spec Kit artifacts in `specs/033-artifact-engine-deploy/`
- [ ] T002 [P] Inspect existing deployment API/core/persistence test fixtures in `tests/Elsa.Platform.Deployment.Core.Tests/` and `tests/Elsa.Platform.Api.Tests/`
- [ ] T003 [P] Inspect existing console deployment test utilities in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared domain contracts and persistence shape that all user stories depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 Update artifact type default apply capability to canonical IDs in `src/Elsa.Platform.Deployment.Artifacts/ArtifactTypeRegistry.cs`
- [ ] T005 Normalize legacy artifact compatibility hints to canonical apply capabilities in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [ ] T006 Add deployability and per-artifact command domain models in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentDeployabilityModels.cs`
- [ ] T007 Extend runtime command models for multi-artifact payloads and per-artifact outcomes in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandModels.cs`
- [ ] T008 Extend deployment command DTO contracts for multi-artifact payloads and runtime reports in `src/Elsa.Platform.Api/Workspace/RuntimeCommandContracts.cs`
- [ ] T009 Update EF deployment command entity mapping for serialized artifact items and outcomes in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs` and `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [ ] T010 Update deployment command store serialization/deserialization for artifact lists and outcomes in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Prove Artifact And Engine Compatibility Before Deployment (Priority: P1) MVP

**Goal**: Operators can select a target engine and see a structured deployability result before any command is queued.

**Independent Test**: Register one workflow-definition artifact, one compatible engine, and one incompatible or stale engine. Deployability marks the compatible engine deployable and blocks the other with canonical capability or freshness remediation.

### Tests for User Story 1

- [ ] T011 [P] [US1] Add deployability service tests for compatible and missing-capability engines in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentDeployabilityServiceTests.cs`
- [ ] T012 [P] [US1] Add deployability tests for stale or missing engine capability metadata in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentDeployabilityServiceTests.cs`
- [ ] T013 [P] [US1] Add deployability tests for archived, invalid, unsupported, and unavailable artifacts in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentDeployabilityServiceTests.cs`
- [ ] T014 [P] [US1] Add deployability tests for unsupported schema version and 10-artifact/10-engine performance in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentDeployabilityServiceTests.cs`
- [ ] T015 [P] [US1] Add deployability endpoint tests in `tests/Elsa.Platform.Api.Tests/Workspace/DeploymentDeployabilityEndpointTests.cs`
- [ ] T016 [P] [US1] Add console deployability rendering tests in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [ ] T017 [US1] Implement `DeploymentDeployabilityService` in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentDeployabilityService.cs`
- [ ] T018 [US1] Register `DeploymentDeployabilityService` in `src/Elsa.Platform.Api/Program.cs`
- [ ] T019 [US1] Add workspace deployability request/response contracts in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [ ] T020 [US1] Add revision deployability endpoint in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [ ] T021 [US1] Replace queue-time artifact compatibility exception logic with deployability service reuse in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentRunService.cs`
- [ ] T022 [US1] Add console deployability API types and client function in `src/Elsa.Platform.Console/src/features/deployments/deploymentModels.ts` and `src/Elsa.Platform.Console/src/features/deployments/deploymentApi.ts`
- [ ] T023 [US1] Add revision detail deployability preflight UI in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 1 is functional and testable independently.

---

## Phase 4: User Story 2 - Queue A Deployment Command With Downloadable Artifact Payloads (Priority: P1)

**Goal**: A compatible deployment queues one run and one runtime command containing all revision artifact records and safe lease-scoped download instructions.

**Independent Test**: Deploy a compatible revision and verify a single run, command, idempotency key, safe artifact item list, and history event are created without raw payload content or local paths.

### Tests for User Story 2

- [ ] T024 [P] [US2] Add command creation tests for multi-artifact payloads and idempotency in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentRunServiceTests.cs`
- [ ] T025 [P] [US2] Add EF persistence tests for command artifact item serialization in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceStoreTests.cs`
- [ ] T026 [P] [US2] Add API queue conflict tests for blocked deployability in `tests/Elsa.Platform.Api.Tests/Workspace/WorkspaceDeploymentRunEndpointTests.cs`

### Implementation for User Story 2

- [ ] T027 [US2] Build deployment command artifact item extraction from revision records in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentRunService.cs`
- [ ] T028 [US2] Persist command artifact item lists when creating runs and commands in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T029 [US2] Include artifact item lists in runtime command DTOs and cockpit run command summaries in `src/Elsa.Platform.Api/Workspace/RuntimeCommandContracts.cs`
- [ ] T030 [US2] Ensure command/run safe payloads redact local paths and unsafe diagnostics in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T031 [US2] Update console run and revision surfaces to show safe artifact names and wrapping digests in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Runtime Applies The Artifact And Reports Outcome (Priority: P2)

**Goal**: A runtime worker can claim a command, download referenced artifact payloads through a lease-authorized path, and report per-artifact progress/final outcomes.

**Independent Test**: Claim a queued command, download the artifact with the active lease, report success and failure cases, and verify run status, observed digest, runtime reference, and per-artifact outcomes.

### Tests for User Story 3

- [ ] T032 [P] [US3] Add runtime download lease authorization tests in `tests/Elsa.Platform.Api.Tests/Workspace/RuntimeArtifactDownloadEndpointTests.cs`
- [ ] T033 [P] [US3] Add command service tests for digest mismatch, per-artifact progress, and final outcomes in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentCommandServiceTests.cs`
- [ ] T034 [P] [US3] Add EF persistence tests for per-artifact outcomes and partial failure in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceStoreTests.cs`

### Implementation for User Story 3

- [ ] T035 [US3] Add command lease validation helper for runtime downloads in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T036 [US3] Add runtime artifact download service method in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [ ] T037 [US3] Add runtime artifact download endpoint in `src/Elsa.Platform.Api/Workspace/RuntimeCommandEndpoints.cs`
- [ ] T038 [US3] Accept and persist per-artifact progress/final outcome reports in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T039 [US3] Mark partial runtime apply as failed or recovery-required while preserving per-artifact outcomes in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`

**Checkpoint**: User Stories 1, 2, and 3 all work independently.

---

## Phase 6: User Story 4 - Guide Users To Resolve Deployment Blockers (Priority: P3)

**Goal**: Users see distinct, actionable remediation for missing capabilities, stale engine metadata, unavailable payloads, archived artifacts, unsupported schema, tier/permission blockers, and duplicate queue attempts.

**Independent Test**: Set up common blocked states and verify each state presents a distinct safe reason and remediation action on the revision detail page without requiring logs.

### Tests for User Story 4

- [ ] T040 [P] [US4] Add deployability remediation mapping tests in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentDeployabilityServiceTests.cs`
- [ ] T041 [P] [US4] Add console blocker copy and disabled reason tests in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 4

- [ ] T042 [US4] Add stable blocker remediation codes in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentDeployabilityService.cs`
- [ ] T043 [US4] Render blocker remediation rows and deterministic disabled reasons in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [ ] T044 [US4] Hide non-applicable revision requirement controls by tier capability in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: All user stories are independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: End-to-end verification, safety hardening, and documentation alignment.

- [ ] T045 [P] Update quickstart verification notes if implemented endpoint names differ in `specs/033-artifact-engine-deploy/quickstart.md`
- [ ] T046 [P] Add or update API contract documentation if runtime DTO fields differ in `specs/033-artifact-engine-deploy/contracts/`
- [ ] T047 Run focused .NET tests for deployment core, EF persistence, and API projects listed in `specs/033-artifact-engine-deploy/quickstart.md`
- [ ] T048 Run console deployment tests and typecheck from `src/Elsa.Platform.Console/`
- [ ] T049 Run `git diff --check` from repository root
- [ ] T050 Run `/speckit-analyze` equivalent review across `specs/033-artifact-engine-deploy/spec.md`, `plan.md`, and `tasks.md`
- [ ] T051 Self-review implementation diff for constitution, safety, and critical regression issues before PR

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on User Story 1 deployability decisions.
- **User Story 3 (Phase 5)**: Depends on User Story 2 command payload shape.
- **User Story 4 (Phase 6)**: Depends on User Story 1 blocker data and can be refined after US2/US3.
- **Polish (Phase 7)**: Depends on selected stories being complete.

### User Story Dependencies

- **US1**: Independent after Foundational; validates deployability without mutating state.
- **US2**: Requires US1 deployability service so queueing and command creation share one authoritative check.
- **US3**: Requires US2 command artifact item payloads so runtime downloads can be command scoped.
- **US4**: Builds on US1 blocker data and US2/US3 runtime failure cases.

### Parallel Opportunities

- T002 and T003 can run in parallel.
- US1 tests T011-T016 can be authored in parallel because they target different layers.
- US2 tests T024-T026 can be authored in parallel.
- US3 tests T032-T034 can be authored in parallel.
- US4 tests T040-T041 can be authored in parallel.
- Documentation tasks T045-T046 can run in parallel with final test execution.

---

## Parallel Example: User Story 1

```bash
Task: "Add deployability service tests for compatible and missing-capability engines in tests/Elsa.Platform.Deployment.Core.Tests/DeploymentDeployabilityServiceTests.cs"
Task: "Add deployability endpoint tests in tests/Elsa.Platform.Api.Tests/Workspace/DeploymentDeployabilityEndpointTests.cs"
Task: "Add console deployability rendering tests in src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1) until deployability is visible and blocks unsafe deployments before queueing.
3. Validate US1 with focused .NET and console tests.

### Incremental Delivery

1. Add US2 to create safe multi-artifact command payloads.
2. Add US3 to enable lease-scoped runtime downloads and per-artifact runtime outcomes.
3. Add US4 blocker guidance and tier-dynamic revision controls.
4. Run quickstart verification commands and self-review before PR.

### Notes

- Every task uses exact repository paths for traceability.
- Mark each completed task as `[X]` in this file during implementation.
- Keep command payloads safe: no raw artifact content, workflow definitions, secrets, credentials, provider tokens, connection strings, or local paths.
