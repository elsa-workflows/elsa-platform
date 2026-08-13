# Tasks: Deployment Engine MVP

**Input**: Design documents from `specs/020-deployment-engine/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Test tasks are included because the spec requires independently testable validation, planning, apply, history, and boundary behavior.

**Organization**: Tasks are grouped by user story to enable independently testable increments.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the engine package and test project.

- [x] T001 Create `src/ValenceControl.Deployment.Engine/ValenceControl.Deployment.Engine.csproj` referencing `src/ValenceControl.Deployment.Abstractions/ValenceControl.Deployment.Abstractions.csproj`
- [x] T002 Create `tests/ValenceControl.Deployment.Engine.Tests/ValenceControl.Deployment.Engine.Tests.csproj` referencing engine, abstractions, xUnit and its built-in assertions
- [x] T003 Add engine source and test projects to `ValenceControl.sln`
- [x] T004 [P] Create `src/ValenceControl.Deployment.Engine/DeploymentEngineDiagnosticCodes.cs`
- [x] T005 [P] Create `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineTestFixtures.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared abstraction and engine infrastructure required by validation, dry-run, apply, and history.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T006 Add `ReadResourcesAsync` to `src/ValenceControl.Deployment.Abstractions/Artifacts/IArtifactReader.cs` and update abstraction contract fixtures/tests
- [x] T007 Add `src/ValenceControl.Deployment.Abstractions/DeploymentExecutionContext.cs` carrying optional actor and prune preference
- [x] T008 Update `src/ValenceControl.Deployment.Abstractions/IDeploymentEngine.cs` to accept optional `DeploymentExecutionContext` for validate, diff, dry-run, and apply operations
- [x] T009 Create `src/ValenceControl.Deployment.Engine/DeploymentEngineOptions.cs` for deployment ID and plan ID generation
- [x] T010 Create `src/ValenceControl.Deployment.Engine/ResourceHandlerRegistry.cs` for resource-type handler lookup and duplicate registration diagnostics
- [x] T011 Create `src/ValenceControl.Deployment.Engine/InMemoryDeploymentHistoryStore.cs` implementing `IDeploymentHistoryStore`
- [x] T012 Create `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs` skeleton implementing `IDeploymentEngine`
- [x] T013 [P] Add history store tests in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineHistoryTests.cs`
- [x] T014 [P] Add dependency boundary tests in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineBoundaryTests.cs`

**Checkpoint**: Foundation ready; user story implementation can start.

---

## Phase 3: User Story 1 - Validate Desired Deployment (Priority: P1) MVP

**Goal**: Validate desired resources and reject unsupported, duplicate, or handler-invalid resources before planning or apply.

**Independent Test**: Submit valid and invalid artifact reader test doubles to the engine and verify diagnostics without handler apply calls.

### Tests for User Story 1

- [x] T015 [P] [US1] Add successful validation test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineValidationTests.cs`
- [x] T016 [P] [US1] Add unsupported resource type validation test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineValidationTests.cs`
- [x] T017 [P] [US1] Add duplicate resource identity validation test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineValidationTests.cs`
- [x] T018 [P] [US1] Add handler diagnostic validation test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineValidationTests.cs`

### Implementation for User Story 1

- [x] T019 [US1] Implement artifact validation and resource extraction via `IArtifactReader.ReadResourcesAsync` in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T020 [US1] Implement duplicate resource identity detection in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T021 [US1] Implement handler lookup and unsupported resource diagnostics in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T022 [US1] Implement handler validation aggregation in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T023 [US1] Return validation `DeploymentResult` statuses and diagnostics in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`

**Checkpoint**: Validation is functional and independently testable.

---

## Phase 4: User Story 2 - Produce Dry-Run Plan (Priority: P2)

**Goal**: Diff desired resources against target state and produce deterministic dry-run plans without mutation.

**Independent Test**: Use in-memory handlers to verify create, update, no-op, delete, unsupported, and deterministic ordering behavior.

### Tests for User Story 2

- [x] T024 [P] [US2] Add create/update/no-op diff tests in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs`
- [x] T025 [P] [US2] Add opt-in delete/prune diff test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs`
- [x] T026 [P] [US2] Add deterministic plan ordering test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs`
- [x] T027 [P] [US2] Add dry-run no-mutation/no-history test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs`

### Implementation for User Story 2

- [x] T028 [US2] Implement `DiffAsync` resource state reading and handler diff routing in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T029 [US2] Implement create/update/no-op action selection support in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T030 [US2] Implement opt-in prune/delete planning from `DeploymentExecutionContext.Prune` in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T031 [US2] Implement deterministic change ordering in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T032 [US2] Implement `DryRunAsync` resource result generation without state or history mutation in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`

**Checkpoint**: Dry-run planning is functional and independently testable.

---

## Phase 5: User Story 3 - Apply Plan And Record History (Priority: P3)

**Goal**: Apply ready changes through handlers, represent partial failures, and record apply history.

**Independent Test**: Use in-memory handlers and history store to verify apply results, skipped no-ops, partial failures, and recorded history.

### Tests for User Story 3

- [x] T033 [P] [US3] Add successful apply and history test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineApplyTests.cs`
- [x] T034 [P] [US3] Add no-op skipped apply test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineApplyTests.cs`
- [x] T035 [P] [US3] Add partial failure and retryable diagnostic test in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineApplyTests.cs`
- [x] T036 [P] [US3] Add apply history audit fields test, including actor from execution context, in `tests/ValenceControl.Deployment.Engine.Tests/DeploymentEngineHistoryTests.cs`

### Implementation for User Story 3

- [x] T037 [US3] Implement `ApplyAsync` handler invocation for create/update/delete changes in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T038 [US3] Implement no-op and blocked change skipping in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T039 [US3] Implement apply status mapping for applied, no-op, partial, and failed outcomes in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T040 [US3] Implement apply history record creation through `IDeploymentHistoryStore` in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`
- [x] T041 [US3] Ensure handler/history exceptions return diagnostics instead of escaping in `src/ValenceControl.Deployment.Engine/DeploymentEngine.cs`

**Checkpoint**: Apply and history complete the Phase 1 loop.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Align docs and verify the full slice.

- [x] T042 [P] Update `specs/020-deployment-engine/quickstart.md` if implementation names or setup differ
- [x] T043 [P] Update `specs/020-deployment-engine/contracts/engine-contract.md` with any final contract adjustments
- [x] T044 Run `dotnet test tests/ValenceControl.Deployment.Engine.Tests/ValenceControl.Deployment.Engine.Tests.csproj`
- [x] T045 Run full solution `dotnet test`
- [x] T046 Run `git diff --check`
- [x] T047 Mark completed tasks in `specs/020-deployment-engine/tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on User Story 1 validation helpers and registry behavior.
- **User Story 3 (Phase 5)**: Depends on User Story 2 plans.
- **Polish (Phase 6)**: Depends on completed selected user stories.

### Parallel Opportunities

- T004 and T005 can run in parallel.
- T013 and T014 can run in parallel after T006-T012 skeletons exist.
- T015-T018 can run in parallel before T019-T023 implementation.
- T024-T027 can run in parallel before T028-T032 implementation.
- T033-T036 can run in parallel before T037-T041 implementation.
- T042 and T043 can run in parallel during polish.

## Parallel Example: User Story 2

```text
Task: "Add create/update/no-op diff tests in tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs"
Task: "Add opt-in delete/prune diff test in tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs"
Task: "Add deterministic plan ordering test in tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs"
Task: "Add dry-run no-mutation/no-history test in tests/ValenceControl.Deployment.Engine.Tests/DeploymentEnginePlanningTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Complete User Story 1.
3. Verify validation independently before planning/apply.

### Incremental Delivery

1. Validation proves handler routing and diagnostics.
2. Dry-run proves deterministic planning without mutation.
3. Apply plus history completes the Phase 1 deployment loop.
