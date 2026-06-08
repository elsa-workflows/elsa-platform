# Tasks: Engine Credential Secret Stores

**Input**: Design documents from `/specs/035-engine-secret-stores/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include focused backend and console tests because the feature changes security-sensitive credential handling and setup UX.

**Organization**: Tasks are grouped by user story so each story remains independently testable.

## Phase 1: Setup

**Purpose**: Confirm existing implementation shape and align shared feature artifacts.

- [x] T001 Review current deployment secret-store models, API contracts, persistence, migrations, and console wizard in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`, `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentContracts.cs`, `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`, and `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T002 Validate Spec Kit artifacts for this feature in `specs/035-engine-secret-stores/spec.md`, `specs/035-engine-secret-stores/plan.md`, `specs/035-engine-secret-stores/data-model.md`, `specs/035-engine-secret-stores/contracts/`, and `specs/035-engine-secret-stores/quickstart.md`

---

## Phase 2: Foundational

**Purpose**: Shared model, validation, and storage changes needed before story-specific behavior.

- [x] T003 Add engine credential store type and credential assignment status contracts in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`
- [x] T004 Add local protected secret metadata fields to EF entities/configuration in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs` and `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [x] T005 Add SQLite and SQL Server migrations for store type and protected secret metadata in `src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [x] T006 Update API request/response DTOs for store type, secret value submission, rotation, usage, and deferred credential assignment in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [x] T007 Wire local secret protection at the API boundary in `src/Elsa.Platform.Api/Program.cs` and `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`

**Checkpoint**: Shared contracts can represent explicit store types, local protected secret presence, and deferred engine credentials without exposing raw values.

---

## Phase 3: User Story 1 - Create Engine Credential Store (Priority: P1)

**Goal**: Administrators can create active workspace engine credential stores for every supported type.

**Independent Test**: Create one store for each supported type and verify it appears as an active engine credential store option with engine-only copy.

### Tests for User Story 1

- [x] T008 [P] [US1] Add API tests for creating supported store types in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [x] T009 [P] [US1] Add persistence tests for store type storage and legacy provider compatibility in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [x] T010 [P] [US1] Add console tests for store type choices and engine-only help text in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [x] T011 [US1] Implement store type validation and provider display derivation in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [x] T012 [US1] Persist and project store type in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T013 [US1] Expose store type through deployment API endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [x] T014 [US1] Update console deployment API/model types for store type in `src/Elsa.Platform.Console/src/features/deployments/deploymentApi.ts` and `src/Elsa.Platform.Console/src/features/deployments/deploymentModels.ts`
- [x] T015 [US1] Update secret-store creation UI copy and store type selector in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 1 is independently functional.

---

## Phase 4: User Story 2 - Add Engine Credential References (Priority: P1)

**Goal**: Administrators can add local encrypted credential references and external locator-only references without exposing raw values.

**Independent Test**: Add a reference to each store type and verify local values are protected/presence-only while external references reject raw secret values.

### Tests for User Story 2

- [x] T016 [P] [US2] Add API tests for local secret submission, external secret rejection, and rotation in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [x] T017 [P] [US2] Add persistence tests proving protected secret presence is stored but not projected as raw value in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [x] T018 [P] [US2] Add console tests for local secret field, external locator-only fields, and no raw value rendering in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 2

- [x] T019 [US2] Implement credential reference validation for local versus external store types in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [x] T020 [US2] Persist protected secret presence and update timestamps in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T021 [US2] Implement create/update/rotate endpoint behavior in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [x] T022 [US2] Update console credential reference forms for local secret entry and external locator-only entry in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 2 is independently functional.

---

## Phase 5: User Story 3 - Register Engines With Optional Credentials (Priority: P1)

**Goal**: Administrators can register engines with either an assigned credential reference or explicit deferred credentials.

**Independent Test**: Register an engine with no credential reference, verify deferred status, then assign a reference later without recreating the engine.

### Tests for User Story 3

- [x] T023 [P] [US3] Add service/API tests for deferred engine registration and later credential assignment in `tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs` and `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [x] T024 [P] [US3] Add persistence tests for nullable engine credential assignment status in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [x] T025 [P] [US3] Add console wizard tests for deferred credential path and later assignment status in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 3

- [x] T026 [US3] Allow explicit deferred credentials in engine service validation in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [x] T027 [US3] Persist and project deferred credential state for engines in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T028 [US3] Expose deferred credential assignment through engine API endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [x] T029 [US3] Update create application wizard and engine registration UI to create credentials or defer them in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 3 is independently functional.

---

## Phase 6: User Story 4 - Maintain Credential Store Lifecycle (Priority: P2)

**Goal**: Administrators can see usage before lifecycle changes and manage credentials without breaking existing engines blindly.

**Independent Test**: Assign one reference to multiple engines, open usage, and verify affected engines are visible before archive/rotation/change.

### Tests for User Story 4

- [x] T030 [P] [US4] Add API tests for credential usage response in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [x] T031 [P] [US4] Add persistence tests for credential usage projection across applications/environments/engines in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [x] T032 [P] [US4] Add console tests for usage warnings before credential reference archival in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 4

- [x] T033 [US4] Add credential usage query contract to `src/Elsa.Platform.Deployment.Core/Workspace/IWorkspaceDeploymentStore.cs` and `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`
- [x] T034 [US4] Implement credential usage projection in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T035 [US4] Expose credential usage endpoint in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [x] T036 [US4] Show usage and lifecycle warning UI in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 4 is independently functional.

---

## Phase 7: Polish & Cross-Cutting Verification

**Purpose**: Validate the full feature and remove critical issues before PR.

- [x] T037 [P] Run `dotnet test Elsa.Platform.sln --filter "WorkspaceDeployment|DeploymentWorkspace|WorkspaceDeploymentService"` from repository root
- [x] T038 [P] Run `npm run test -- src/features/deployments/DeploymentsPage.test.tsx` from `src/Elsa.Platform.Console`
- [x] T039 [P] Run `npm run typecheck` from `src/Elsa.Platform.Console`
- [x] T040 Run `dotnet build Elsa.Platform.sln` from repository root
- [x] T041 Run `git diff --check` from repository root
- [x] T042 Perform self-review of credential safety, deferred engine behavior, migration compatibility, and console UX using the spec and quickstart in `specs/035-engine-secret-stores/`
- [x] T043 Address all critical self-review findings in the affected source and test files

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **User Stories (Phases 3-6)**: Depend on Foundational completion. US1, US2, and US3 are all P1 but should be implemented in order because references depend on stores and engine assignment depends on references/deferred state.
- **Polish (Phase 7)**: Depends on selected user stories being complete; for this feature, all stories are required.

### User Story Dependencies

- **US1**: Starts after Foundation; no story dependency.
- **US2**: Depends on US1 store type behavior.
- **US3**: Depends on Foundation; integrates with US1/US2 when assigned credentials exist, but deferred credentials work independently.
- **US4**: Depends on US2 and US3 to have references and engine assignments to report.

### Parallel Opportunities

- T008, T009, and T010 can run in parallel.
- T016, T017, and T018 can run in parallel.
- T023, T024, and T025 can run in parallel.
- T030, T031, and T032 can run in parallel.
- T037, T038, and T039 can run in parallel after implementation.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Complete US1 to create typed engine credential stores.
3. Complete US2 enough to create safe credential references.
4. Complete US3 deferred engine registration so setup no longer blocks on credentials.

### Full Delivery

1. Add typed store contracts and persistence.
2. Add local protected secret and external locator behavior.
3. Add explicit deferred engine credential assignment.
4. Add credential usage/lifecycle visibility.
5. Run verification, self-review, PR, Copilot loop, and merge.
