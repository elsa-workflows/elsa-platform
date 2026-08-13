# Tasks: Deployment Setup Domain Flow

**Input**: Design documents from `/specs/033-deployment-setup-domain-flow/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required for API/persistence contracts and console setup flows because this feature changes user-facing deployment setup behavior and persistence contracts.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup

**Purpose**: Align feature documentation and existing deployment setup surfaces.

- [X] T001 Update `AGENTS.md` Spec Kit plan reference to `specs/033-deployment-setup-domain-flow/plan.md`
- [X] T002 [P] Review existing deployment setup form code in `src/ValenceControl.Console/src/features/deployments/DeploymentSetupPanel.tsx`
- [X] T003 [P] Review existing deployment workspace persistence code in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`

---

## Phase 2: Foundational

**Purpose**: Add shared secret-store/reference domain contracts and persistence needed by engine registration pickers.

- [X] T004 Add secret-store and credential-reference models and requests in `src/ValenceControl.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`
- [X] T005 Add secret-store and credential-reference store/service operations in `src/ValenceControl.Deployment.Core/Workspace/IWorkspaceDeploymentStore.cs` and `src/ValenceControl.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T006 Add secret-store and credential-reference EF entities/configuration in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs` and `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T007 Add SQLite and SQL Server migrations for deployment secret stores, credential references, and optional engine credential-reference linkage in migration projects
- [X] T008 Implement secret-store and credential-reference persistence in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T009 Add API contracts and endpoints for secret stores and credential references in `src/ValenceControl.Api/Workspace/WorkspaceDeploymentContracts.cs` and `src/ValenceControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T010 Add TypeScript models, API calls, and query keys for secret stores and credential references in deployment console files

---

## Phase 3: User Story 1 - Create Environment Without Engine Coupling (Priority: P1)

**Goal**: Environment creation uses only environment fields and creates no engine.

**Independent Test**: Create an application environment and verify no engine registration is required or created.

### Tests for User Story 1

- [X] T011 [P] [US1] Add API test proving environment creation does not require engine data in `tests/ValenceControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [X] T012 [P] [US1] Add console test proving Add environment form only renders environment fields in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [X] T013 [US1] Replace Add environment usage of combined setup panel with environment-only form in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T014 [US1] Update Add environment success navigation and empty-state copy in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: A new environment can be created without an engine and the UI points users to the next engine-registration action.

---

## Phase 4: User Story 2 - Register Engines Inside An Environment (Priority: P1)

**Goal**: Engine registration is an environment-scoped action with clear field labels.

**Independent Test**: Register an engine from an environment page and verify it appears in the environment engine list.

### Tests for User Story 2

- [X] T015 [P] [US2] Add API test for engine registration with selected credential reference metadata in `tests/ValenceControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [X] T016 [P] [US2] Add console test for no-engine empty state and Register engine action in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 2

- [X] T017 [US2] Update engine registration panel labels and validation in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T018 [US2] Ensure engine registration derives provider/reference from selected metadata in API/service code while preserving legacy string request support

**Checkpoint**: Engine registration is discoverable only after an environment exists and uses clear engine terminology.

---

## Phase 5: User Story 3 - Choose Registered Secret References (Priority: P2)

**Goal**: Engine registration uses active secret store/reference options rather than opaque free text.

**Independent Test**: Register a secret store and reference, then register an engine by selecting those options.

### Tests for User Story 3

- [X] T019 [P] [US3] Add API tests for creating/listing/archiving secret stores and credential references in `tests/ValenceControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [X] T020 [P] [US3] Add persistence tests for active/archived secret store and credential reference behavior in `tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [X] T021 [P] [US3] Add console tests for secret-store and credential-reference picker behavior in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 3

- [X] T022 [US3] Add deployment setup option loading to `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T023 [US3] Replace engine credential provider/reference free-text controls with secret-store/reference pickers in engine setup panels
- [X] T024 [US3] Preserve legacy credential display for existing engines without registered reference metadata

**Checkpoint**: Users can register an engine from registered credential metadata and archived options are excluded.

---

## Phase 6: User Story 4 - Manage Secret Store Metadata (Priority: P2)

**Goal**: Deployment setup includes a simple management surface for secret stores and credential references.

**Independent Test**: Create and archive secret stores/references from the console and verify engine registration options update.

### Tests for User Story 4

- [X] T025 [P] [US4] Add console tests for secret store/reference management interactions in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 4

- [X] T026 [US4] Add a Secret stores setup panel to deployment application or environment setup surfaces in `src/ValenceControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T027 [US4] Wire create/archive mutations and query invalidation for secret stores and credential references in console deployment code

**Checkpoint**: The console can manage safe credential metadata needed by engine registration pickers.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verify, clean up, and document the finished feature.

- [X] T028 Run API tests with `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --no-restore`
- [X] T029 Run persistence tests with `dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --no-restore --filter DeploymentWorkspace`
- [X] T030 Run console typecheck with `npm run typecheck` from `src/ValenceControl.Console`
- [X] T031 Run console tests with `npm test -- DeploymentsPage` from `src/ValenceControl.Console`
- [X] T032 Run `git diff --check`
- [X] T033 Perform self-review and fix high-priority issues before PR

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 has no dependencies.
- Phase 2 depends on Phase 1 and blocks secret-store/reference picker stories.
- US1 can proceed once existing environment APIs are confirmed.
- US2 depends on US1 UI placement and foundational credential metadata contracts.
- US3 depends on foundational secret-store/reference APIs.
- US4 depends on US3 models/API calls.
- Polish depends on selected user stories being complete.

### User Story Dependencies

- **US1**: Can be delivered first as the MVP.
- **US2**: Depends on environment pages exposing engine registration after environment creation.
- **US3**: Depends on secret-store/reference metadata contracts.
- **US4**: Depends on secret-store/reference API and console models.

### Parallel Opportunities

- T002/T003 can run in parallel.
- Model/API/persistence tests for different layers can be prepared in parallel after foundational contracts are understood.
- Console tests for environment-only setup and engine-registration empty state can be prepared before implementation.

## Implementation Strategy

1. Deliver the domain split first: environment-only creation plus environment-scoped engine registration.
2. Add metadata registry APIs and persistence without raw secret storage.
3. Replace credential free text with registered metadata pickers.
4. Add management UI for the metadata registry.
5. Run full focused verification and self-review before PR.
