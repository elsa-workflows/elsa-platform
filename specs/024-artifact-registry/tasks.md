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
- [x] T003 [P] Add artifact core test fixture helpers in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentTestFixtures.cs`
- [x] T004 [P] Add artifact API test helpers in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTestFixtures.cs`

---

## Phase 2: Foundational - Models, Store Contracts, And Persistence

**Purpose**: Shared artifact registry models and persistence support that block all user stories.

**Checkpoint**: Artifact metadata can be represented, stored, queried by workspace, and migrated.

- [x] T005 Define workspace artifact request/result models in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactModels.cs`
- [x] T006 Define artifact store contract in `src/ElsaControl.Deployment.Core/Workspace/IWorkspaceArtifactStore.cs`
- [x] T007 Add artifact validation and registration service skeleton in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T008 Add EF artifact entity in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [x] T009 Add EF artifact mappings in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [x] T010 Add artifact store methods to `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T011 Add SQLite and SQL Server migrations for artifact registry metadata in `src/ElsaControl.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/ElsaControl.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [x] T012 Register artifact services and store contracts in `src/ElsaControl.Api/Program.cs`

---

## Phase 3: User Story 1 - Register Artifact Metadata (Priority: P1) MVP

**Goal**: Authorized workspace users can register immutable artifact metadata and references without storing payloads.

**Independent Test**: Register an artifact through API, reload list/detail, verify identity/digest/manifest/resource/reference/audit metadata, duplicate handling, and absence of payload/secret values.

### Tests for User Story 1

- [x] T013 [P] [US1] Add core artifact registration validation tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceArtifactServiceTests.cs`
- [x] T014 [P] [US1] Add artifact persistence round-trip and duplicate tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [x] T015 [P] [US1] Add artifact registration API permission, duplicate, and safe response tests in `tests/ElsaControl.Api.Tests/WorkspaceArtifactApiTests.cs`

### Implementation for User Story 1

- [x] T016 [US1] Implement artifact registration validation and duplicate metadata behavior in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T017 [US1] Implement artifact persistence registration behavior in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T018 [US1] Add artifact registration endpoint and request/response contracts in `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs` and `src/ElsaControl.Api/Workspace/WorkspaceArtifactContracts.cs`
- [x] T019 [US1] Run focused US1 checks documented in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact registration is independently functional through API.

---

## Phase 4: User Story 2 - Inspect Registered Artifacts (Priority: P1)

**Goal**: Workspace members can list and inspect registered artifacts through a real Artifacts console view.

**Independent Test**: Seed workspace artifacts, open `/admin/artifacts`, verify live list/detail/empty/error states, safe diagnostics, permission behavior, and workspace isolation.

### Tests for User Story 2

- [x] T020 [P] [US2] Add artifact list/detail API isolation and performance tests in `tests/ElsaControl.Api.Tests/WorkspaceArtifactApiTests.cs`
- [x] T021 [P] [US2] Add Artifacts page list/detail/empty tests in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 2

- [x] T022 [US2] Implement artifact list/detail store queries in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T023 [US2] Add artifact list/detail endpoints in `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T024 [US2] Add artifact API client and models in `src/ElsaControl.Console/src/features/artifacts/artifactApi.ts` and `src/ElsaControl.Console/src/features/artifacts/artifactModels.ts`
- [x] T025 [US2] Implement Artifacts page list/detail/register shell in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.tsx`
- [x] T026 [US2] Enable Artifacts navigation and route in `src/ElsaControl.Console/src/app/AppShell.tsx` and `src/ElsaControl.Console/src/app/routes.tsx`
- [x] T027 [US2] Run focused US2 checks documented in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact inspection is independently functional through API and console.

---

## Phase 5: User Story 3 - Refresh Artifact Inspection Status (Priority: P2)

**Goal**: Authorized users can refresh inspection state from supported references and see valid/invalid diagnostics update without changing immutable identity.

**Independent Test**: Refresh a valid local/test artifact reference and an invalid/mismatched reference, verify statuses and diagnostics update safely, and verify unsupported references fail closed.

### Tests for User Story 3

- [x] T028 [P] [US3] Add core inspection refresh service tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceArtifactServiceTests.cs`
- [x] T029 [P] [US3] Add persistence tests for inspection status updates preserving identity in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [x] T030 [P] [US3] Add refresh API permission, invalid reference, and unsupported provider tests in `tests/ElsaControl.Api.Tests/WorkspaceArtifactApiTests.cs`
- [x] T031 [P] [US3] Add console refresh inspection tests in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 3

- [x] T032 [US3] Implement local/test artifact inspection adapter in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T033 [US3] Implement inspection status update persistence in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T034 [US3] Add artifact refresh endpoint in `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T035 [US3] Add refresh action and invalid/unsupported states in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.tsx`
- [x] T036 [US3] Run focused US3 checks documented in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact inspection refresh is independently functional and fail-closed.

---

## Phase 6: Polish And Verification

**Purpose**: Final documentation, safety review, and broad verification.

- [x] T037 [P] Update quickstart results and known limitations in `specs/024-artifact-registry/quickstart.md`
- [x] T038 [P] Update final API contract examples in `specs/024-artifact-registry/contracts/artifact-registry-api.md`
- [x] T039 [P] Update final console UX contract examples in `specs/024-artifact-registry/contracts/console-artifacts-ux.md`
- [x] T040 Review payload/secret redaction, workspace permission checks, duplicate handling, cross-workspace rejection, and unsupported-reference handling across `src/ElsaControl.Deployment.Core/Workspace/`, `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T041 Run `dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --filter WorkspaceArtifact` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T042 Run `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T043 Run `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceArtifact` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T044 Run `cd src/ElsaControl.Console && npm test -- --run artifacts` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T045 Run `cd src/ElsaControl.Console && npm run typecheck` and record result in `specs/024-artifact-registry/quickstart.md`
- [x] T046 Run `git diff --check` and record result in `specs/024-artifact-registry/quickstart.md`

---

## Phase 7: Follow-up User Story 4 - Upload Artifact Payload (Priority: P2)

**Goal**: Authorized workspace users can upload a deployment artifact ZIP from the console. The backend stores bytes in artifact blob storage, derives artifact metadata server-side, and creates the same immutable registry record shape used by manual registration.

**Independent Test**: Upload a valid artifact ZIP through API and console, verify the artifact record is created with computed digest, manifest summary, resources, storage reference, inspection state, and no raw payload in catalog/API responses. Invalid, duplicate, oversized, unsafe ZIP, expired-session, and cross-workspace completion cases fail closed with safe diagnostics.

### Tests for User Story 4

- [ ] T047 [P] [US4] Add core upload-session validation and inspection tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceArtifactUploadServiceTests.cs`
- [ ] T048 [P] [US4] Add artifact blob storage contract tests with local/test provider in `tests/ElsaControl.Deployment.Core.Tests/ArtifactBlobStorageTests.cs`
- [ ] T049 [P] [US4] Add persistence tests for upload sessions, expiration, completion, duplicate content, and cleanup state in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactUploadPersistenceTests.cs`
- [ ] T050 [P] [US4] Add API tests for upload session create, content upload, completion, abort, permission checks, idempotency, unsafe ZIP rejection, and cross-workspace rejection in `tests/ElsaControl.Api.Tests/WorkspaceArtifactUploadApiTests.cs`
- [ ] T051 [P] [US4] Add console upload wizard tests for file selection, progress states, completion navigation, failed diagnostics, permission-blocked state, and absence of raw payload/secret output in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 4

- [ ] T052 [US4] Define artifact upload request/result models and upload status enums in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactUploadModels.cs`
- [ ] T053 [US4] Define artifact blob storage abstraction and local/test provider in `src/ElsaControl.Deployment.Core/Workspace/IWorkspaceArtifactBlobStore.cs`
- [ ] T054 [US4] Add upload-session store contract methods in `src/ElsaControl.Deployment.Core/Workspace/IWorkspaceArtifactStore.cs`
- [ ] T055 [US4] Add upload session EF entity, mappings, and migrations in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`, `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`, and migration projects
- [ ] T056 [US4] Implement upload session create, idempotency, completion, expiration, abort, duplicate detection, and cleanup state in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactUploadService.cs`
- [ ] T057 [US4] Implement ZIP safety validation, digest computation, artifact envelope inspection, manifest/resource extraction, size limits, entry-count limits, path traversal rejection, and safe diagnostics in the upload service
- [ ] T058 [US4] Register artifact blob storage and upload services in `src/ElsaControl.Api/Program.cs`
- [ ] T059 [US4] Add upload endpoints and contracts in `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs` and `src/ElsaControl.Api/Workspace/WorkspaceArtifactContracts.cs`
- [ ] T060 [US4] Add artifact upload API client/models in `src/ElsaControl.Console/src/features/artifacts/artifactApi.ts` and `src/ElsaControl.Console/src/features/artifacts/artifactModels.ts`
- [ ] T061 [US4] Replace primary inline manual registration UX with a dedicated Upload artifact page/wizard in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.tsx`
- [ ] T062 [US4] Keep manual Register artifact available as an advanced/reference-registration path, separated from the primary upload flow
- [ ] T063 [US4] Add upload quickstart coverage and verification results in `specs/024-artifact-registry/quickstart.md`

**Checkpoint**: Artifact upload is independently functional, derives metadata from uploaded payloads, and keeps catalog persistence payload-free.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1; blocks all user stories.
- **US1 Register Artifact Metadata**: Depends on Phase 2; MVP.
- **US2 Inspect Registered Artifacts**: Depends on Phase 2 and uses US1 records but remains independently testable with seeded records.
- **US3 Refresh Artifact Inspection Status**: Depends on Phase 2 and extends US1/US2 records.
- **Phase 6 Polish**: Depends on selected user stories being complete.
- **US4 Upload Artifact Payload**: Follow-up slice; depends on US1/US2 artifact registry records and can be implemented after metadata registration/list/detail are stable.

### User Story Dependencies

- **US1 (P1)**: First independently deliverable slice.
- **US2 (P1)**: Can proceed after shared models/store contracts exist.
- **US3 (P2)**: Can proceed after artifact records exist.
- **US4 (P2)**: Can proceed after US1/US2; extends registry ingestion and shares artifact inspection concepts with US3.

### Parallel Opportunities

- T001-T004 can run in parallel.
- T013-T015 can run in parallel after Phase 2.
- T020-T021 can run in parallel after Phase 2.
- T028-T031 can run in parallel after Phase 2.
- T037-T039 can run in parallel during polish.
- T047-T051 can run in parallel before US4 implementation.
- T052-T055 can run in parallel after upload contracts are agreed.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 artifact registration.
3. Validate through API, persistence, and core tests.

### Incremental Delivery

1. Add Artifacts list/detail console view after registration is working.
2. Add inspection refresh after records and UI state are stable.
3. Add upload ingestion as a follow-up slice with blob storage and server-side inspection.
4. Run broad verification and update quickstart/contracts.
