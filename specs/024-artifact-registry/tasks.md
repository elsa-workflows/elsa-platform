# Tasks: Deployment Artifact Registry

**Input**: Design documents from `specs/024-artifact-registry/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because this feature changes workspace persistence, permission checks, API isolation, safe artifact metadata handling, and a user-facing console route.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks.
- **[Story]**: User story label from `spec.md`.
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare shared contracts and test scaffolding for artifact registry work.

- [x] T001 [P] Add artifact API contract examples in `specs/024-artifact-registry/contracts/artifact-registry-api.md`
- [x] T002 [P] Add console Artifacts UX contract examples in `specs/024-artifact-registry/contracts/console-artifacts-ux.md`
- [ ] T003 [P] Add artifact core test fixture helpers in `tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceDeploymentTestFixtures.cs`
- [ ] T004 [P] Add artifact API test helpers in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTestFixtures.cs`

---

## Phase 2: Foundational - Models, Store Contracts, And Persistence

**Purpose**: Shared artifact registry models and persistence support that block all user stories.

**Checkpoint**: Artifact metadata can be represented, stored, queried by workspace, and migrated.

- [x] T005 Define workspace artifact request/result models in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceArtifactModels.cs`
- [x] T006 Define artifact store contract in `src/Elsa.Platform.Deployment.Core/Workspace/IWorkspaceArtifactStore.cs`
- [x] T007 Add artifact validation and registration service skeleton in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T008 Add EF artifact entity in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [x] T009 Add EF artifact mappings in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [x] T010 Add artifact store methods to `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T011 Add SQLite and SQL Server migrations for artifact registry metadata in `src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [x] T012 Register artifact services and store contracts in `src/Elsa.Platform.Api/Program.cs`

---

## Phase 3: User Story 1 - Register Artifact Metadata (Priority: P1) MVP

**Goal**: Authorized workspace users can register immutable artifact metadata and references without storing payloads.

**Independent Test**: Register an artifact through API, reload list/detail, verify identity/digest/manifest/resource/reference/audit metadata, duplicate handling, and absence of payload/secret values.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add core artifact registration validation tests in `tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceArtifactServiceTests.cs`
- [ ] T014 [P] [US1] Add artifact persistence round-trip and duplicate tests in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [ ] T015 [P] [US1] Add artifact registration API permission, duplicate, and safe response tests in `tests/Elsa.Platform.Api.Tests/WorkspaceArtifactApiTests.cs`

### Implementation for User Story 1

- [x] T016 [US1] Implement artifact registration validation and duplicate metadata behavior in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T017 [US1] Implement artifact persistence registration behavior in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T018 [US1] Add artifact registration endpoint and request/response contracts in `src/Elsa.Platform.Api/Workspace/WorkspaceArtifactEndpoints.cs` and `src/Elsa.Platform.Api/Workspace/WorkspaceArtifactContracts.cs`
- [ ] T019 [US1] Run focused US1 checks documented in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact registration is independently functional through API.

---

## Phase 4: User Story 2 - Inspect Registered Artifacts (Priority: P1)

**Goal**: Workspace members can list and inspect registered artifacts through a real Artifacts console view.

**Independent Test**: Seed workspace artifacts, open `/admin/artifacts`, verify live list/detail/empty/error states, safe diagnostics, permission behavior, and workspace isolation.

### Tests for User Story 2

- [ ] T020 [P] [US2] Add artifact list/detail API isolation and performance tests in `tests/Elsa.Platform.Api.Tests/WorkspaceArtifactApiTests.cs`
- [x] T021 [P] [US2] Add Artifacts page list/detail/empty tests in `src/Elsa.Platform.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 2

- [x] T022 [US2] Implement artifact list/detail store queries in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T023 [US2] Add artifact list/detail endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T024 [US2] Add artifact API client and models in `src/Elsa.Platform.Console/src/features/artifacts/artifactApi.ts` and `src/Elsa.Platform.Console/src/features/artifacts/artifactModels.ts`
- [x] T025 [US2] Implement Artifacts page list/detail/register shell in `src/Elsa.Platform.Console/src/features/artifacts/ArtifactsPage.tsx`
- [x] T026 [US2] Enable Artifacts navigation and route in `src/Elsa.Platform.Console/src/app/AppShell.tsx` and `src/Elsa.Platform.Console/src/app/routes.tsx`
- [ ] T027 [US2] Run focused US2 checks documented in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact inspection is independently functional through API and console.

---

## Phase 5: User Story 3 - Refresh Artifact Inspection Status (Priority: P2)

**Goal**: Authorized users can refresh inspection state from supported references and see valid/invalid diagnostics update without changing immutable identity.

**Independent Test**: Refresh a valid local/test artifact reference and an invalid/mismatched reference, verify statuses and diagnostics update safely, and verify unsupported references fail closed.

### Tests for User Story 3

- [ ] T028 [P] [US3] Add core inspection refresh service tests in `tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceArtifactServiceTests.cs`
- [ ] T029 [P] [US3] Add persistence tests for inspection status updates preserving identity in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [ ] T030 [P] [US3] Add refresh API permission, invalid reference, and unsupported provider tests in `tests/Elsa.Platform.Api.Tests/WorkspaceArtifactApiTests.cs`
- [x] T031 [P] [US3] Add console refresh inspection tests in `src/Elsa.Platform.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 3

- [x] T032 [US3] Implement local/test artifact inspection adapter in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T033 [US3] Implement inspection status update persistence in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T034 [US3] Add artifact refresh endpoint in `src/Elsa.Platform.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T035 [US3] Add refresh action and invalid/unsupported states in `src/Elsa.Platform.Console/src/features/artifacts/ArtifactsPage.tsx`
- [ ] T036 [US3] Run focused US3 checks documented in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact inspection refresh is independently functional and fail-closed.

---

## Phase 6: Polish And Verification

**Purpose**: Final documentation, safety review, and broad verification.

- [x] T037 [P] Update quickstart results and known limitations in `specs/024-artifact-registry/quickstart.md`
- [x] T038 [P] Update final API contract examples in `specs/024-artifact-registry/contracts/artifact-registry-api.md`
- [x] T039 [P] Update final console UX contract examples in `specs/024-artifact-registry/contracts/console-artifacts-ux.md`
- [x] T040 Review payload/secret redaction, workspace permission checks, duplicate handling, cross-workspace rejection, and unsupported-reference handling across `src/Elsa.Platform.Deployment.Core/Workspace/`, `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/Elsa.Platform.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [ ] T041 Run `dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter WorkspaceArtifact` and record result in `specs/024-artifact-registry/quickstart.md`
- [ ] T042 Run `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact` and record result in `specs/024-artifact-registry/quickstart.md`
- [ ] T043 Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceArtifact` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T044 Run `cd src/Elsa.Platform.Console && npm test -- --run artifacts` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T045 Run `cd src/Elsa.Platform.Console && npm run typecheck` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T046 Run `git diff --check` and record result in `specs/024-artifact-registry/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1; blocks all user stories.
- **US1 Register Artifact Metadata**: Depends on Phase 2; MVP.
- **US2 Inspect Registered Artifacts**: Depends on Phase 2 and uses US1 records but remains independently testable with seeded records.
- **US3 Refresh Artifact Inspection Status**: Depends on Phase 2 and extends US1/US2 records.
- **Phase 6 Polish**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 (P1)**: First independently deliverable slice.
- **US2 (P1)**: Can proceed after shared models/store contracts exist.
- **US3 (P2)**: Can proceed after artifact records exist.

### Parallel Opportunities

- T001-T004 can run in parallel.
- T013-T015 can run in parallel after Phase 2.
- T020-T021 can run in parallel after Phase 2.
- T028-T031 can run in parallel after Phase 2.
- T037-T039 can run in parallel during polish.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 artifact registration.
3. Validate through API, persistence, and core tests.

### Incremental Delivery

1. Add Artifacts list/detail console view after registration is working.
2. Add inspection refresh after records and UI state are stable.
3. Run broad verification and update quickstart/contracts.
