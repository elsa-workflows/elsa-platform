# Tasks: Engine Health Verification

**Input**: Design documents from `specs/023-engine-health-verification/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because this feature changes workspace safety gates, persisted engine metadata, API authorization, heartbeat freshness, and user-facing console workflows.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks.
- **[Story]**: User story label from `spec.md`.
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare shared contracts and test scaffolding for engine health verification.

- [x] T001 [P] Add engine health API contract examples in `specs/023-engine-health-verification/contracts/engine-health-api.md`
- [x] T002 [P] Add console verification UX contract examples in `specs/023-engine-health-verification/contracts/console-engine-health-ux.md`
- [x] T003 [P] Add engine health core test fixture helpers in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentTestFixtures.cs`
- [x] T004 [P] Add engine health API test helpers in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTestFixtures.cs`

---

## Phase 2: Foundational - Models, Store Contracts, And Persistence

**Purpose**: Shared engine health models and persistence support that block all user stories.

**Checkpoint**: Engine health metadata can be represented, stored, projected into cockpit data, and migrated.

- [x] T005 Define engine health request/result models in `src/ElsaControl.Deployment.Core/Workspace/EngineHealthModels.cs`
- [x] T006 Extend workflow engine cockpit metadata with verification fields in `src/ElsaControl.Deployment.Core/Cockpit/DeploymentCockpitModels.cs`
- [x] T007 Extend workspace engine models and store contracts in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs` and `src/ElsaControl.Deployment.Core/Workspace/IWorkspaceDeploymentStore.cs`
- [x] T008 Add engine health service skeleton and health classification in `src/ElsaControl.Deployment.Core/Workspace/EngineHealthService.cs`
- [x] T009 Add EF engine verification fields and optional event entity in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [x] T010 Add EF mappings for engine verification metadata in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [x] T011 Implement engine health persistence methods and cockpit projection in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T012 Add SQLite and SQL Server migrations for engine verification metadata in `src/ElsaControl.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/ElsaControl.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`

---

## Phase 3: User Story 1 - Verify Registered Engine Health (Priority: P1) MVP

**Goal**: Authorized workspace users can manually verify a registered engine and see persisted health metadata update.

**Independent Test**: Register an engine, trigger verification, reload cockpit, and verify health/version/certificate/credential/heartbeat metadata and safe diagnostics update without raw secrets.

### Tests for User Story 1

- [x] T013 [P] [US1] Add core verification classification tests in `tests/ElsaControl.Deployment.Core.Tests/EngineHealthServiceTests.cs`
- [x] T014 [P] [US1] Add persistence round-trip tests for manual verification metadata in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [x] T015 [P] [US1] Add manual verification API permission and success/failure tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentEngineHealthTests.cs`
- [x] T016 [P] [US1] Add console manual verification tests in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [x] T017 [US1] Implement manual verification service behavior in `src/ElsaControl.Deployment.Core/Workspace/EngineHealthService.cs`
- [x] T018 [US1] Add manual verification endpoint and response contract in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs` and `src/ElsaControl.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [x] T019 [US1] Add manual verification API client and models in `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts` and `src/ElsaControl.Console/src/features/deployments/deploymentModels.ts`
- [x] T020 [US1] Add Verify action, pending state, safe diagnostics, and cockpit refresh in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T021 [US1] Run focused US1 checks documented in `specs/023-engine-health-verification/quickstart.md`

**Checkpoint**: Manual verification is independently functional through API and console.

---

## Phase 4: User Story 2 - Accept Engine Heartbeats (Priority: P1)

**Goal**: Authorized runtime/platform callers can update a registered engine's heartbeat metadata without overwriting newer state or unrelated capabilities.

**Independent Test**: Submit heartbeat metadata for one workspace-owned engine, reload cockpit, verify metadata updates only for that engine, and verify stale/cross-workspace updates fail.

### Tests for User Story 2

- [x] T022 [P] [US2] Add heartbeat freshness and capability preservation tests in `tests/ElsaControl.Deployment.Core.Tests/EngineHealthServiceTests.cs`
- [x] T023 [P] [US2] Add heartbeat persistence tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [x] T024 [P] [US2] Add heartbeat API authorization, stale update, and cross-workspace tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentEngineHealthTests.cs`

### Implementation for User Story 2

- [x] T025 [US2] Implement heartbeat update behavior in `src/ElsaControl.Deployment.Core/Workspace/EngineHealthService.cs`
- [x] T026 [US2] Implement capability preservation and optional capability update persistence in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T027 [US2] Add heartbeat endpoint and request contract in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs` and `src/ElsaControl.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [x] T028 [US2] Run focused US2 checks documented in `specs/023-engine-health-verification/quickstart.md`

**Checkpoint**: Heartbeats are independently functional and stale/cross-workspace updates fail closed.

---

## Phase 5: User Story 3 - Show Verification State In Console (Priority: P2)

**Goal**: Console users can understand unverified, verifying, healthy, degraded, and unreachable engine states and why runtime controls are unavailable.

**Independent Test**: Render engines in each state and verify badges, timestamps, diagnostics, Verify availability, and runtime control availability.

### Tests for User Story 3

- [x] T029 [P] [US3] Add console state coverage for healthy, degraded, unreachable, and permission-blocked verification states in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [x] T030 [P] [US3] Add runtime control health-gate tests in `tests/ElsaControl.Deployment.Core.Tests/RuntimeControlServiceTests.cs`

### Implementation for User Story 3

- [x] T031 [US3] Render last verification, heartbeat freshness, and safe diagnostics in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T032 [US3] Update runtime controls messaging for unreachable/unverified engines in `src/ElsaControl.Console/src/features/deployments/RuntimeControlsPanel.tsx`
- [x] T033 [US3] Ensure server-side runtime control health gates use verification metadata in `src/ElsaControl.Deployment.Core/Workspace/RuntimeControlService.cs`
- [x] T034 [US3] Run focused US3 checks documented in `specs/023-engine-health-verification/quickstart.md`

**Checkpoint**: Console health states are independently understandable and runtime controls fail closed.

---

## Phase 6: Polish And Verification

**Purpose**: Final documentation, safety review, and broad verification.

- [x] T035 [P] Update quickstart results and known limitations in `specs/023-engine-health-verification/quickstart.md`
- [x] T036 [P] Update final API contract examples in `specs/023-engine-health-verification/contracts/engine-health-api.md`
- [x] T037 [P] Update final console UX contract examples in `specs/023-engine-health-verification/contracts/console-engine-health-ux.md`
- [x] T038 Review secret redaction, workspace permission checks, stale heartbeat handling, cross-workspace rejection, and runtime-control health gates across `src/ElsaControl.Deployment.Core/Workspace/`, `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [x] T039 Run `dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --filter EngineHealth` and record result in `specs/023-engine-health-verification/quickstart.md`
- [x] T040 Run `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspacePersistenceTests` and record result in `specs/023-engine-health-verification/quickstart.md`
- [x] T041 Run `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceDeploymentEngineHealth` and record result in `specs/023-engine-health-verification/quickstart.md`
- [x] T042 Run `cd src/ElsaControl.Console && npm test -- --run deployments` and record result in `specs/023-engine-health-verification/quickstart.md`
- [x] T043 Run `cd src/ElsaControl.Console && npm run typecheck` and record result in `specs/023-engine-health-verification/quickstart.md`
- [x] T044 Run `git diff --check` and record result in `specs/023-engine-health-verification/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1; blocks all user stories.
- **US1 Manual Verification**: Depends on Phase 2; MVP.
- **US2 Heartbeats**: Depends on Phase 2; can proceed after shared models/store contracts exist.
- **US3 Console Health States**: Depends on US1 and shared cockpit metadata; integrates runtime-control messaging.
- **Phase 6 Polish**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 (P1)**: First independently deliverable slice.
- **US2 (P1)**: Can be implemented after Phase 2 and shares health persistence with US1.
- **US3 (P2)**: Uses US1/US2 metadata to complete user-facing state explanations.

### Parallel Opportunities

- T001-T004 can run in parallel.
- T013-T016 can run in parallel after Phase 2.
- T022-T024 can run in parallel after Phase 2.
- T035-T037 can run in parallel during polish.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 manual verification.
3. Validate through API, persistence, core, and console tests.

### Incremental Delivery

1. Add heartbeat ingestion after manual verification is working.
2. Add richer console health states and runtime-control messaging.
3. Run broad verification and update quickstart/contracts.
