# Tasks: Runtime Command Sync

**Input**: Design documents from `specs/028-runtime-command-sync/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because this feature changes deployment run execution semantics, runtime-facing authorization, idempotency, lease/claim behavior, recovery behavior, persistence, and safe diagnostics.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks.
- **[Story]**: User story label from `spec.md`.
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare command contracts and shared test scaffolding.

- [ ] T001 [P] Add runtime command API examples in `specs/028-runtime-command-sync/contracts/runtime-command-api.md`
- [ ] T002 [P] Add console command history UX examples in `specs/028-runtime-command-sync/contracts/console-command-history-ux.md`
- [ ] T003 [P] Add command lifecycle test fixtures in `tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceDeploymentTestFixtures.cs`
- [ ] T004 [P] Add runtime command API test helpers in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTestFixtures.cs`

---

## Phase 2: Foundational - Command Models, Store Contract, And Persistence

**Purpose**: Shared command lifecycle models and persistence support that block all user stories.

**Checkpoint**: Deployment commands can be represented, stored, queried, and linked to deployment runs.

- [ ] T005 Define command, lease, attempt, progress, result, and webhook models in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandModels.cs`
- [ ] T006 Define command store contract in `src/Elsa.Platform.Deployment.Core/Workspace/IWorkspaceDeploymentCommandStore.cs`
- [ ] T007 Add command service skeleton and lifecycle validation in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T008 Extend deployment run queueing to create command records in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentRunService.cs`
- [ ] T009 Add EF command, command event, and webhook notification entities in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [ ] T010 Add EF command mappings in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [ ] T011 Implement command store methods in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T012 Add SQLite and SQL Server migrations for runtime command metadata in `src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [ ] T013 Register command services and store contracts in `src/Elsa.Platform.Api/Program.cs`

---

## Phase 3: User Story 1 - Runtime Polls And Claims Commands (Priority: P1) MVP

**Goal**: A target runtime can poll and claim exactly one command lease without duplicate delivery.

**Independent Test**: Queue a run, poll pending commands, claim one command, and verify a second worker cannot claim it while the lease is active.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add core poll/claim and lease exclusivity tests in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentCommandServiceTests.cs`
- [ ] T015 [P] [US1] Add persistence poll ordering and active lease tests in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentCommandPersistenceTests.cs`
- [ ] T016 [P] [US1] Add runtime poll/claim API authorization and conflict tests in `tests/Elsa.Platform.Api.Tests/RuntimeCommandApiTests.cs`

### Implementation for User Story 1

- [ ] T017 [US1] Implement poll and claim lifecycle behavior in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T018 [US1] Implement pending command queries and claim persistence in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T019 [US1] Add runtime command poll and claim endpoints in `src/Elsa.Platform.Api/Workspace/RuntimeCommandEndpoints.cs` and `src/Elsa.Platform.Api/Workspace/RuntimeCommandContracts.cs`
- [ ] T020 [US1] Run focused US1 checks documented in `specs/028-runtime-command-sync/quickstart.md`

**Checkpoint**: Runtime pull and claim are independently functional.

---

## Phase 4: User Story 2 - Runtime Reports Progress And Completion (Priority: P1)

**Goal**: Claimed commands can heartbeat, report progress, and complete/fail/reject while updating deployment history.

**Independent Test**: Claim a command, post heartbeat/progress, complete or fail it, and verify command state and run history are updated with safe diagnostics.

### Tests for User Story 2

- [ ] T021 [P] [US2] Add heartbeat, progress, complete, fail, and reject core tests in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentCommandServiceTests.cs`
- [ ] T022 [P] [US2] Add persistence tests for command final state and run history projection in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentCommandPersistenceTests.cs`
- [ ] T023 [P] [US2] Add runtime heartbeat/progress/complete/fail/reject API tests in `tests/Elsa.Platform.Api.Tests/RuntimeCommandApiTests.cs`

### Implementation for User Story 2

- [ ] T024 [US2] Implement heartbeat, progress, complete, fail, and reject behavior in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T025 [US2] Implement command event and final result persistence in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T026 [US2] Add runtime heartbeat/progress/complete/fail/reject endpoints in `src/Elsa.Platform.Api/Workspace/RuntimeCommandEndpoints.cs`
- [ ] T027 [US2] Project command events into run detail/history in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentRunService.cs` and `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [ ] T028 [US2] Run focused US2 checks documented in `specs/028-runtime-command-sync/quickstart.md`

**Checkpoint**: Runtime command outcomes are visible through deployment run history.

---

## Phase 5: User Story 3 - Recover Stale Or Duplicate Deliveries (Priority: P2)

**Goal**: Stale and duplicate command deliveries do not cause duplicate apply and become explicit recovery states.

**Independent Test**: Let a lease become stale, run recovery, duplicate webhook/claim/completion calls, and verify deterministic state.

### Tests for User Story 3

- [ ] T029 [P] [US3] Add stale lease and duplicate final-state core tests in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentCommandServiceTests.cs`
- [ ] T030 [P] [US3] Add stale command recovery persistence tests in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentCommandPersistenceTests.cs`
- [ ] T031 [P] [US3] Add duplicate delivery and duplicate completion API tests in `tests/Elsa.Platform.Api.Tests/RuntimeCommandApiTests.cs`

### Implementation for User Story 3

- [ ] T032 [US3] Implement stale command recovery and idempotent final-state handling in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T033 [US3] Bridge existing in-process queue worker to command claim/complete semantics in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentQueueWorker.cs`
- [ ] T034 [US3] Persist stale recovery and duplicate delivery events in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T035 [US3] Run focused US3 checks documented in `specs/028-runtime-command-sync/quickstart.md`

**Checkpoint**: Duplicate delivery and stale recovery fail closed.

---

## Phase 6: User Story 4 - Trigger Fetch Via Webhook Without Authority Transfer (Priority: P3)

**Goal**: Webhook notifications can trigger runtime fetch without becoming deployment authority.

**Independent Test**: Queue a command with webhook enabled, record a safe notification, deliver it twice, and verify polling/claiming remains authoritative.

### Tests for User Story 4

- [ ] T036 [P] [US4] Add webhook trigger record tests in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentCommandServiceTests.cs`
- [ ] T037 [P] [US4] Add webhook notification persistence tests in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentCommandPersistenceTests.cs`
- [ ] T038 [P] [US4] Add webhook-triggered fetch API/contract tests in `tests/Elsa.Platform.Api.Tests/RuntimeCommandApiTests.cs`

### Implementation for User Story 4

- [ ] T039 [US4] Add webhook notification models and creation behavior in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandModels.cs` and `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentCommandService.cs`
- [ ] T040 [US4] Persist webhook notification records/events in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T041 [US4] Expose safe webhook trigger metadata where needed in `src/Elsa.Platform.Api/Workspace/RuntimeCommandContracts.cs`
- [ ] T042 [US4] Run focused US4 checks documented in `specs/028-runtime-command-sync/quickstart.md`

**Checkpoint**: Webhook-triggered fetch is modeled as notification only.

---

## Phase 7: Console History And Verification

**Purpose**: Surface command lifecycle safely and run focused verification.

- [ ] T043 [P] Update deployment run models/API response contracts with command lifecycle summary in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [ ] T044 [P] Update console deployment history rendering if command events are not already visible in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [ ] T045 [P] Add console command history tests if UI changes are needed in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [ ] T046 Review payload/secret redaction, command authorization, lease token handling, duplicate delivery, stale recovery, and run history projection across `src/Elsa.Platform.Deployment.Core/Workspace/`, `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/Elsa.Platform.Api/Workspace/RuntimeCommandEndpoints.cs`
- [ ] T047 Run `dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter DeploymentCommand` and record result in `specs/028-runtime-command-sync/quickstart.md`
- [ ] T048 Run `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentCommand` and record result in `specs/028-runtime-command-sync/quickstart.md`
- [ ] T049 Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand` and record result in `specs/028-runtime-command-sync/quickstart.md`
- [ ] T050 Run `cd src/Elsa.Platform.Console && npm test -- --run deployments` and record result in `specs/028-runtime-command-sync/quickstart.md`
- [ ] T051 Run `cd src/Elsa.Platform.Console && npm run typecheck` and record result in `specs/028-runtime-command-sync/quickstart.md`
- [ ] T052 Run `git diff --check` and record result in `specs/028-runtime-command-sync/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1 and blocks all user stories.
- **US1 Runtime Polls And Claims Commands**: Depends on Phase 2; MVP.
- **US2 Runtime Reports Progress And Completion**: Depends on US1 claim/lease behavior.
- **US3 Recover Stale Or Duplicate Deliveries**: Depends on US1 and US2 lifecycle state.
- **US4 Trigger Fetch Via Webhook**: Depends on command creation and poll/claim semantics.
- **Phase 7 Verification**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 (P1)**: First independently deliverable slice.
- **US2 (P1)**: Requires lease ownership from US1.
- **US3 (P2)**: Requires command lifecycle and final-state semantics from US1/US2.
- **US4 (P3)**: Can be implemented after command creation exists.

### Parallel Opportunities

- T001-T004 can run in parallel.
- T014-T016 can run in parallel after Phase 2.
- T021-T023 can run in parallel after US1.
- T029-T031 can run in parallel after US2.
- T036-T038 can run in parallel after command creation exists.
- T043-T045 can run in parallel during console/history work.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 poll/claim behavior.
3. Validate through core, persistence, and API tests.

### Incremental Delivery

1. Add heartbeat/progress/final outcome reporting.
2. Add stale recovery and idempotent duplicate delivery handling.
3. Add webhook-triggered fetch records/events.
4. Surface command lifecycle in deployment history and run broad verification.
