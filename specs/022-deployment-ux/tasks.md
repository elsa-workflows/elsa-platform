# Tasks: Deployment UX

**Input**: Design documents from `specs/022-deployment-ux/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because this feature changes workspace permissions, isolation, deployment persistence, queue execution, confirmation safety, and user-facing console workflows.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks.
- **[Story]**: User story label from `spec.md`.
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare references, contracts, and test scaffolding for durable deployment UX work.

- [X] T001 Add deployment core project reference to `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.csproj`
- [X] T002 [P] Add shared deployment API test helpers in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTestFixtures.cs`
- [X] T003 [P] Add deployment core test fixture helpers in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentTestFixtures.cs`
- [X] T004 [P] Add deployment permissions contract examples in `specs/022-deployment-ux/contracts/workspace-deployment-api.md`
- [X] T005 [P] Add confirmation and queued-run UX notes in `specs/022-deployment-ux/contracts/console-deployments-ux.md`

---

## Phase 2: Foundational - Permissions, Persistence, Queue, And Confirmation

**Purpose**: Core infrastructure that blocks all deployment user stories.

**Checkpoint**: Workspace deployment records, permission grants, confirmations, and queued runs can be persisted and checked by service code.

- [X] T006 Define workspace deployment domain models in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`
- [X] T007 Define workspace permission models and permission IDs in `src/ElsaControl.Deployment.Core/Workspace/WorkspacePermissionModels.cs`
- [X] T008 Define confirmation and queue/run models in `src/ElsaControl.Deployment.Core/Workspace/DeploymentRunModels.cs`
- [X] T009 Define structured desired-state models in `src/ElsaControl.Deployment.Core/Workspace/DesiredStateModels.cs`
- [X] T010 Define observability binding and drift report metadata models in `src/ElsaControl.Deployment.Core/Workspace/ObservabilityDriftModels.cs`
- [X] T011 Define workspace deployment store contract in `src/ElsaControl.Deployment.Core/Workspace/IWorkspaceDeploymentStore.cs`
- [X] T012 Define permission store contract in `src/ElsaControl.Deployment.Core/Workspace/IWorkspacePermissionStore.cs`
- [X] T013 Add workspace permission service skeleton in `src/ElsaControl.Deployment.Core/Workspace/WorkspacePermissionService.cs`
- [X] T014 Add workspace deployment service skeleton in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T015 Add validation/run/queue/control/confirmation/observability service skeletons in `src/ElsaControl.Deployment.Core/Workspace/DeploymentValidationService.cs`, `src/ElsaControl.Deployment.Core/Workspace/DeploymentRunService.cs`, `src/ElsaControl.Deployment.Core/Workspace/DeploymentQueueWorker.cs`, `src/ElsaControl.Deployment.Core/Workspace/RuntimeControlService.cs`, `src/ElsaControl.Deployment.Core/Workspace/ConfirmationService.cs`, and `src/ElsaControl.Deployment.Core/Workspace/ObservabilityDriftService.cs`
- [X] T016 Update cockpit service to use workspace deployment store projections in `src/ElsaControl.Deployment.Core/Cockpit/DeploymentCockpitService.cs`
- [X] T017 Add EF deployment workspace, observability binding, and drift report entities in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [X] T018 Add EF deployment, observability, and drift entity mappings in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T019 Add EF workspace deployment, permission, observability, and drift store in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T020 Add deployment `DbSet` properties in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/CatalogDbContext.cs`
- [X] T021 Register deployment workspace services, permission services, confirmation services, queue worker, and stores in `src/ElsaControl.Api/Program.cs`
- [X] T022 Add SQLite and SQL Server deployment, observability, drift, permission, confirmation, and queued-run migrations in `src/ElsaControl.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/ElsaControl.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`

---

## Phase 3: User Story 1 - View Real Deployment Cockpit (Priority: P1) MVP

**Goal**: Replace seeded cockpit data with persisted workspace records and preserve workspace isolation.

**Independent Test**: Seed one workspace with deployment records and permission grants, read the cockpit as a member, verify only that workspace's records render, and verify non-members are denied.

### Tests for User Story 1

- [X] T023 [P] [US1] Add core cockpit projection tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs`
- [X] T024 [P] [US1] Add EF persistence round-trip tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [X] T025 [P] [US1] Add workspace cockpit isolation API tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentIsolationTests.cs`
- [X] T026 [P] [US1] Add persisted observability and drift metadata API/persistence tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs` and `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [X] T027 [P] [US1] Add normal-dataset cockpit load bounded-query and under-3-second test in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [X] T028 [P] [US1] Add console cockpit live-data tests in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [X] T029 [US1] Implement cockpit projection from persisted workspace deployment records in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T030 [US1] Implement cockpit read methods in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T031 [US1] Replace in-memory cockpit registration in `src/ElsaControl.Api/Program.cs`
- [X] T032 [US1] Add cockpit response contracts in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [X] T033 [US1] Update cockpit endpoint to use durable service and read permission checks in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T034 [US1] Implement persisted observability binding and drift report metadata projection in `src/ElsaControl.Deployment.Core/Workspace/ObservabilityDriftService.cs`, `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T035 [US1] Update console deployment API models for durable cockpit and permission fields in `src/ElsaControl.Console/src/features/deployments/deploymentModels.ts`
- [X] T036 [US1] Update Deployments page empty/loading/error states for live cockpit data in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T037 [US1] Run focused US1 checks documented in `specs/022-deployment-ux/quickstart.md`

**Checkpoint**: User Story 1 is independently functional and the cockpit no longer depends on seeded demo data.

---

## Phase 4: User Story 2 - Register Workflow Engines (Priority: P1)

**Goal**: Authorized workspace users can create applications, environments, and engine registrations with capability and credential-reference metadata.

**Independent Test**: Register an application, environment, and engine through API and console, reload cockpit, verify supported controls are shown, raw credentials are absent, and users without setup permission cannot mutate.

### Tests for User Story 2

- [X] T038 [P] [US2] Add setup permission service tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspacePermissionServiceTests.cs`
- [X] T039 [P] [US2] Add application/environment/engine service tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs`
- [X] T040 [P] [US2] Add registration API permission and entitlement tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentPermissionTests.cs`
- [X] T041 [P] [US2] Add console setup form tests in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 2

- [X] T042 [US2] Implement bootstrap workspace-owner permission grants and effective permission reads in `src/ElsaControl.Deployment.Core/Workspace/WorkspacePermissionService.cs`
- [X] T043 [US2] Implement permission grant persistence in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T044 [US2] Implement application and environment create/update methods in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T045 [US2] Implement engine registration methods with credential redaction and capability validation in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T046 [US2] Implement application/environment/engine write methods in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T047 [US2] Add permissions, application, environment, and engine registration endpoints in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T048 [US2] Add setup and permissions API client calls in `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts`
- [X] T049 [US2] Add setup and engine registration UI in `src/ElsaControl.Console/src/features/deployments/DeploymentSetupPanel.tsx`
- [X] T050 [US2] Wire setup and engine registration UI into `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T051 [US2] Run focused US2 checks documented in `specs/022-deployment-ux/quickstart.md`

**Checkpoint**: User Story 2 is independently functional from API and console.

---

## Phase 5: User Story 3 - Preview Promotion And Validation (Priority: P2)

**Goal**: Workspace members with promotion preview and desired-state permissions can create structured desired-state revisions, compare revisions, and see validation blockers/warnings before mutation.

**Independent Test**: Create two structured desired revisions, preview promotion, verify categorized diff and validation blockers, and prove target state is unchanged.

### Tests for User Story 3

- [X] T052 [P] [US3] Add structured desired-state service tests in `tests/ElsaControl.Deployment.Core.Tests/DeploymentValidationServiceTests.cs`
- [X] T053 [P] [US3] Add desired-state persistence tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [X] T054 [P] [US3] Add promotion preview API permission tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [X] T055 [P] [US3] Add promotion preview console tests in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 3

- [X] T056 [US3] Implement desired-state record validation and deterministic hashing in `src/ElsaControl.Deployment.Core/Workspace/DeploymentValidationService.cs`
- [X] T057 [US3] Implement desired-state revision creation and immutable storage in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T058 [US3] Implement structured desired-state persistence in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T059 [US3] Implement structured desired-state diff and target validation in `src/ElsaControl.Deployment.Core/Workspace/DeploymentValidationService.cs`
- [X] T060 [US3] Add revision and promotion preview endpoints with permission checks in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T061 [US3] Add desired-state and promotion preview API client calls in `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts`
- [X] T062 [US3] Add promotion preview UI in `src/ElsaControl.Console/src/features/deployments/PromotionPreviewPanel.tsx`
- [X] T063 [US3] Wire promotion preview UI into `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T064 [US3] Run focused US3 checks documented in `specs/022-deployment-ux/quickstart.md`

**Checkpoint**: User Story 3 is independently functional, preview is read-only, and desired state is structured platform data.

---

## Phase 6: User Story 4 - Track Deployment Runs And Rollback (Priority: P2)

**Goal**: Authorized users can explicitly confirm and enqueue deployment/rollback runs, inspect durable status/history, and recover safely from worker restarts.

**Independent Test**: Confirm and enqueue a deployment run, verify queued/running/succeeded history and deployed revision state, simulate stale running recovery, then confirm and enqueue rollback.

### Tests for User Story 4

- [X] T065 [P] [US4] Add confirmation service tests for same-user consumption, single-use behavior, expiration, and replay prevention in `tests/ElsaControl.Deployment.Core.Tests/ConfirmationServiceTests.cs`
- [X] T066 [P] [US4] Add deployment run service tests in `tests/ElsaControl.Deployment.Core.Tests/DeploymentRunServiceTests.cs`
- [X] T067 [P] [US4] Add deployment queue worker tests for queued processing, stale claimed run recovery, and no automatic duplicate apply in `tests/ElsaControl.Deployment.Core.Tests/DeploymentQueueWorkerTests.cs`
- [X] T068 [P] [US4] Add deployment run and rollback API tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [X] T069 [P] [US4] Add deployment and rollback API confirmation tests for same-user, single-use, expiration, and replay rejection in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentMutationAuthorizationTests.cs`
- [X] T070 [P] [US4] Add deployment run console tests in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 4

- [X] T071 [US4] Implement action confirmation creation and consumption with same-user, single-use, expiration, and replay protection in `src/ElsaControl.Deployment.Core/Workspace/ConfirmationService.cs`
- [X] T072 [US4] Implement confirmation persistence in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T073 [US4] Implement queued deployment run creation, active-run conflict checks, status updates, and history in `src/ElsaControl.Deployment.Core/Workspace/DeploymentRunService.cs`
- [X] T074 [US4] Implement rollback validation and queued rollback run creation in `src/ElsaControl.Deployment.Core/Workspace/DeploymentRunService.cs`
- [X] T075 [US4] Implement deployment run persistence and append-only history in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T076 [US4] Implement in-process queue worker claim/process/recovery flow where queued runs process normally and stale claimed runs move to `RecoveryRequired` without automatic replay in `src/ElsaControl.Deployment.Core/Workspace/DeploymentQueueWorker.cs`
- [X] T077 [US4] Register hosted queue worker in `src/ElsaControl.Api/Program.cs`
- [X] T078 [US4] Add confirmation, run, rollback, and run-detail endpoints in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T079 [US4] Add confirmation, run, rollback, and run-detail API client calls in `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts`
- [X] T080 [US4] Add run history, confirmation, and rollback UI in `src/ElsaControl.Console/src/features/deployments/DeploymentRunsPanel.tsx`
- [X] T081 [US4] Wire run history and rollback UI into `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T082 [US4] Run focused US4 checks documented in `specs/022-deployment-ux/quickstart.md`

**Checkpoint**: User Story 4 is independently functional and run history persists after refresh/restart.

---

## Phase 7: User Story 5 - Use Capability-Gated Runtime Controls (Priority: P3)

**Goal**: Supported controls are exposed and executed only when capability, permission, entitlement, and confirmation checks pass.

**Independent Test**: Register engines with different capabilities, verify UI control availability, and verify direct unsupported/unconfirmed/unauthorized control requests are rejected.

### Tests for User Story 5

- [X] T083 [P] [US5] Add runtime control service tests in `tests/ElsaControl.Deployment.Core.Tests/RuntimeControlServiceTests.cs`
- [X] T084 [P] [US5] Add runtime control API tests for unsupported capability, missing confirmation, same-user confirmation, single-use confirmation, and unauthorized permission rejection in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentMutationAuthorizationTests.cs`
- [X] T085 [P] [US5] Add runtime control console tests in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 5

- [X] T086 [US5] Implement capability-gated and confirmation-gated runtime control execution in `src/ElsaControl.Deployment.Core/Workspace/RuntimeControlService.cs`
- [X] T087 [US5] Implement runtime control audit persistence in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T088 [US5] Add runtime control endpoint with permission and confirmation checks in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T089 [US5] Add runtime control API client calls in `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts`
- [X] T090 [US5] Add runtime controls UI in `src/ElsaControl.Console/src/features/deployments/RuntimeControlsPanel.tsx`
- [X] T091 [US5] Wire runtime controls UI into `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T092 [US5] Run focused US5 checks documented in `specs/022-deployment-ux/quickstart.md`

**Checkpoint**: User Story 5 is independently functional and unsupported or unconfirmed controls fail closed.

---

## Phase 8: User Story 6 - Replace Demo UX With Working Console Flows (Priority: P3)

**Goal**: Complete the console workflow using live APIs for setup, preview, queued deploy, rollback, history, metadata-only observability/drift, and controls.

**Independent Test**: Use the console to complete setup, preview, confirmation, queued deployment, history inspection, rollback, and a supported control without direct API calls.

### Tests for User Story 6

- [X] T093 [P] [US6] Add console integration coverage for complete deployment flow in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [X] T094 [P] [US6] Add E2E deployment smoke test in `tests/ElsaControl.Console.E2E/deployments.spec.ts`

### Implementation for User Story 6

- [X] T095 [US6] Remove remaining demo-only notices and seeded assumptions from `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T096 [US6] Add query invalidation and mutation refresh paths in `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts`
- [X] T097 [US6] Add permission-blocked, confirmation-required, queued, running, succeeded, failed, and recovery states in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T098 [US6] Add metadata-only observability and drift rendering in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T099 [US6] Run console and E2E checks documented in `specs/022-deployment-ux/quickstart.md`

**Checkpoint**: User Story 6 completes the live deployment UX.

---

## Phase 9: Polish And Verification

**Purpose**: Final hardening, docs, and broad verification.

- [X] T100 [P] Update quickstart results and known limitations in `specs/022-deployment-ux/quickstart.md`
- [X] T101 [P] Update deployment contracts for final response shapes in `specs/022-deployment-ux/contracts/workspace-deployment-api.md`
- [X] T102 [P] Update console UX contract for final states in `specs/022-deployment-ux/contracts/console-deployments-ux.md`
- [X] T103 Review secret redaction, permission checks, observability/drift metadata safety, confirmation checks, and audit metadata across `src/ElsaControl.Deployment.Core/Workspace/`, `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T104 Run `dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj` and record result in `specs/022-deployment-ux/quickstart.md`
- [X] T105 Run `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj` and record result in `specs/022-deployment-ux/quickstart.md`
- [X] T106 Run `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceDeployment` and record result in `specs/022-deployment-ux/quickstart.md`
- [X] T107 Run `cd src/ElsaControl.Console && npm test -- --run deployments` and record result in `specs/022-deployment-ux/quickstart.md`
- [X] T108 Run `cd src/ElsaControl.Console && npm run typecheck` and record result in `specs/022-deployment-ux/quickstart.md`
- [X] T109 Run `dotnet test ElsaControl.sln` and record result in `specs/022-deployment-ux/quickstart.md`
- [X] T110 Run `git diff --check` and record result in `specs/022-deployment-ux/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **US1 View Real Deployment Cockpit (Phase 3)**: Depends on Foundational and is the MVP.
- **US2 Register Workflow Engines (Phase 4)**: Depends on Foundational and benefits from US1 cockpit projection.
- **US3 Preview Promotion And Validation (Phase 5)**: Depends on US1 and US2 records.
- **US4 Track Deployment Runs And Rollback (Phase 6)**: Depends on US3 validation, structured desired-state records, and confirmation foundation.
- **US5 Capability-Gated Runtime Controls (Phase 7)**: Depends on US2 engine capabilities and confirmation foundation.
- **US6 Replace Demo UX (Phase 8)**: Depends on the desired backend slices being complete.
- **Polish (Phase 9)**: Depends on completed selected user stories.

### User Story Dependencies

- **US1 (P1)**: MVP durable read cockpit.
- **US2 (P1)**: Registration can run after foundational permission/store models but should integrate with US1 cockpit.
- **US3 (P2)**: Requires applications, environments, engines, permissions, and structured revisions.
- **US4 (P2)**: Requires preview validation, structured revisions, confirmations, queue state, and run history.
- **US5 (P3)**: Requires registered engine capabilities, permissions, and confirmations.
- **US6 (P3)**: Integrates all completed backend stories into final UX.

### Parallel Opportunities

- T002-T005 can run in parallel.
- T006-T012 can be prepared in parallel, but compilation should be finalized sequentially.
- Test-writing tasks within each story can run in parallel.
- US3 and US5 can proceed in parallel after US2 if separate developers own them.
- Polish documentation tasks T100-T102 can run in parallel.

## Parallel Examples

### User Story 1

```text
Task: "Add core cockpit projection tests in tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs"
Task: "Add EF persistence round-trip tests in tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs"
Task: "Add workspace cockpit isolation API tests in tests/ElsaControl.Api.Tests/WorkspaceDeploymentIsolationTests.cs"
Task: "Add console cockpit live-data tests in src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx"
```

### User Story 4

```text
Task: "Add confirmation service tests in tests/ElsaControl.Deployment.Core.Tests/ConfirmationServiceTests.cs"
Task: "Add deployment run service tests in tests/ElsaControl.Deployment.Core.Tests/DeploymentRunServiceTests.cs"
Task: "Add deployment queue worker tests in tests/ElsaControl.Deployment.Core.Tests/DeploymentQueueWorkerTests.cs"
Task: "Add deployment run and rollback API tests in tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 durable cockpit.
3. Validate workspace isolation and console read path before mutations.
4. Stop and demo the real persisted cockpit.

### Incremental Delivery

1. Add flexible deployment permissions and persistence foundation.
2. Add registration so users can create cockpit data.
3. Add structured desired-state revisions and promotion preview.
4. Add confirmations, queued deployment runs, worker processing, and rollback.
5. Add capability-gated runtime controls.
6. Complete console UX and E2E smoke.

### Verification Policy

- Write story tests before implementation tasks.
- Mark tasks `[X]` only after the implementation and focused verification for that task are complete.
- Use narrow test filters at story checkpoints, then run broader solution tests in polish.
