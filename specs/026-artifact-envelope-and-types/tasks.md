# Tasks: Artifact Envelope And Types

**Input**: Design documents from `specs/026-artifact-envelope-and-types/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because this feature changes artifact identity validation, safe metadata handling, persistence shape, API response contracts, duplicate behavior, and console artifact visibility.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks.
- **[Story]**: User story label from `spec.md`.
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare shared contracts and test scaffolding for typed artifact envelopes.

- [x] T001 [P] Add artifact envelope API examples in `specs/026-artifact-envelope-and-types/contracts/artifact-envelope-contract.md`
- [x] T002 [P] Add console envelope UX examples in `specs/026-artifact-envelope-and-types/contracts/console-artifact-envelope-ux.md`
- [x] T003 [P] Add envelope core test fixture helpers in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceDeploymentTestFixtures.cs`
- [x] T004 [P] Add envelope API test helpers in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTestFixtures.cs`

---

## Phase 2: Foundational - Envelope Contracts, Type Registry, And Persistence

**Purpose**: Shared artifact envelope models and type validation that block all user stories.

**Checkpoint**: Envelopes and artifact types can be represented, validated, stored, and projected from legacy records.

- [x] T005 Define artifact envelope, artifact type, producer, payload reference, safe metadata, compatibility hint, and diagnostic models in `src/ElsaControl.Deployment.Artifacts/ArtifactEnvelopeModels.cs` and `src/ElsaControl.Deployment.Artifacts/ArtifactTypeModels.cs`
- [x] T006 Implement built-in artifact type registry with `elsa.workflow-definition` in `src/ElsaControl.Deployment.Artifacts/ArtifactTypeRegistry.cs`
- [x] T007 Implement envelope validation for type IDs, digests, immutable identity, safe metadata, and references in `src/ElsaControl.Deployment.Artifacts/ArtifactEnvelopeValidator.cs`
- [x] T008 Extend workspace artifact models with envelope fields in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactModels.cs`
- [x] T009 Extend workspace artifact service duplicate and legacy projection behavior in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T010 Add EF envelope fields and mappings in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs` and `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [x] T011 Implement envelope persistence and legacy projection in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T012 Add SQLite and SQL Server migrations for envelope metadata in `src/ElsaControl.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/ElsaControl.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`

---

## Phase 3: User Story 1 - Submit Typed Artifact Envelope (Priority: P1) MVP

**Goal**: Authorized producers can submit immutable typed envelopes without storing payloads.

**Independent Test**: Submit a valid `elsa.workflow-definition` envelope, reload list/detail, verify type/producer/digest/reference/compatibility metadata, idempotent duplicate handling, and absence of payload/secret values.

### Tests for User Story 1

- [x] T013 [P] [US1] Add envelope validation and duplicate tests in `tests/ElsaControl.Deployment.Artifacts.Tests/ArtifactEnvelopeValidationTests.cs` and `tests/ElsaControl.Deployment.Core.Tests/WorkspaceArtifactServiceTests.cs`
- [x] T014 [P] [US1] Add envelope persistence round-trip and duplicate conflict tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [x] T015 [P] [US1] Add envelope registration API permission, duplicate, and safe response tests in `tests/ElsaControl.Api.Tests/WorkspaceArtifactApiTests.cs`

### Implementation for User Story 1

- [x] T016 [US1] Wire envelope validation into artifact registration in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T017 [US1] Persist envelope metadata and reject conflicting immutable fields in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [x] T018 [US1] Extend workspace artifact registration request/response contracts in `src/ElsaControl.Api/Workspace/WorkspaceArtifactContracts.cs` and `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T019 [US1] Run focused US1 checks documented in `specs/026-artifact-envelope-and-types/quickstart.md`

**Checkpoint**: Typed envelope registration is independently functional through API.

---

## Phase 4: User Story 2 - Manage Artifact Type Semantics (Priority: P1)

**Goal**: Elsa Control and integration code can rely on stable type IDs and reject unknown or disabled types before deployment planning.

**Independent Test**: Load built-in artifact types, submit envelopes with supported and unsupported type IDs, and verify runtime compatibility hints can be evaluated without payload inspection.

### Tests for User Story 2

- [x] T020 [P] [US2] Add artifact type registry tests in `tests/ElsaControl.Deployment.Artifacts.Tests/ArtifactEnvelopeValidationTests.cs`
- [x] T021 [P] [US2] Add API artifact type discovery and unknown-type rejection tests in `tests/ElsaControl.Api.Tests/WorkspaceArtifactApiTests.cs`

### Implementation for User Story 2

- [x] T022 [US2] Register artifact type services in `src/ElsaControl.Api/Program.cs`
- [x] T023 [US2] Add artifact type discovery endpoint in `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T024 [US2] Add type and compatibility fields to console artifact models/API client in `src/ElsaControl.Console/src/features/artifacts/artifactModels.ts` and `src/ElsaControl.Console/src/features/artifacts/artifactApi.ts`
- [x] T025 [US2] Render artifact type and compatibility summary in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.tsx`
- [x] T026 [US2] Run focused US2 checks documented in `specs/026-artifact-envelope-and-types/quickstart.md`

**Checkpoint**: Artifact type semantics are visible and validated without payload inspection.

---

## Phase 5: User Story 3 - Preserve Safe Metadata Boundaries (Priority: P2)

**Goal**: Safe display/search metadata is useful while payloads and secrets remain outside catalog persistence and API responses.

**Independent Test**: Submit safe and unsafe metadata, verify safe fields display, unsafe fields are rejected/redacted, and persistence contains no payload or secret values.

### Tests for User Story 3

- [x] T027 [P] [US3] Add safe metadata validation tests in `tests/ElsaControl.Deployment.Artifacts.Tests/ArtifactEnvelopeValidationTests.cs`
- [x] T028 [P] [US3] Add persistence safety tests for payload and secret exclusion in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [x] T029 [P] [US3] Add API and console safe metadata tests in `tests/ElsaControl.Api.Tests/WorkspaceArtifactApiTests.cs` and `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 3

- [x] T030 [US3] Enforce safe metadata key/value rules in `src/ElsaControl.Deployment.Artifacts/ArtifactEnvelopeValidator.cs`
- [x] T031 [US3] Shape safe envelope responses in `src/ElsaControl.Api/Workspace/WorkspaceArtifactContracts.cs`
- [x] T032 [US3] Render safe display metadata and diagnostics in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.tsx`
- [x] T033 [US3] Run focused US3 checks documented in `specs/026-artifact-envelope-and-types/quickstart.md`

**Checkpoint**: Envelope metadata is useful for users and safe for catalog/API exposure.

---

## Phase 6: User Story 4 - Maintain Backward Compatibility (Priority: P3)

**Goal**: Existing registry records and manual registration remain compatible with envelope-backed artifact records.

**Independent Test**: Read and register artifacts through the existing metadata path and verify they project with default type/producer fields while current tests still pass.

### Tests for User Story 4

- [x] T034 [P] [US4] Add legacy artifact projection tests in `tests/ElsaControl.Deployment.Core.Tests/WorkspaceArtifactServiceTests.cs`
- [x] T035 [P] [US4] Add migration/backfill persistence tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceArtifactPersistenceTests.cs`
- [x] T036 [P] [US4] Add console legacy artifact rendering tests in `src/ElsaControl.Console/src/features/artifacts/ArtifactsPage.test.tsx`

### Implementation for User Story 4

- [x] T037 [US4] Implement default envelope projection for legacy records in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceArtifactService.cs`
- [x] T038 [US4] Backfill existing artifact rows with default envelope metadata in SQLite and SQL Server migrations
- [x] T039 [US4] Preserve manual registration compatibility in `src/ElsaControl.Api/Workspace/WorkspaceArtifactContracts.cs`
- [x] T040 [US4] Run focused US4 checks documented in `specs/026-artifact-envelope-and-types/quickstart.md`

**Checkpoint**: Existing artifact registry behavior survives the envelope upgrade.

---

## Phase 7: Polish And Verification

**Purpose**: Final documentation, safety review, and broad verification.

- [x] T041 [P] Update quickstart results and known limitations in `specs/026-artifact-envelope-and-types/quickstart.md`
- [x] T042 [P] Update final API contract examples in `specs/026-artifact-envelope-and-types/contracts/artifact-envelope-contract.md`
- [x] T043 [P] Update final console UX contract examples in `specs/026-artifact-envelope-and-types/contracts/console-artifact-envelope-ux.md`
- [x] T044 Review payload/secret redaction, type validation, duplicate handling, compatibility hints, cross-workspace rejection, and legacy projection across `src/ElsaControl.Deployment.Core/`, `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`, and `src/ElsaControl.Api/Workspace/WorkspaceArtifactEndpoints.cs`
- [x] T045 Run `dotnet test tests/ElsaControl.Deployment.Artifacts.Tests/ElsaControl.Deployment.Artifacts.Tests.csproj --filter ArtifactEnvelope` and record result in `specs/026-artifact-envelope-and-types/quickstart.md`
- [x] T046 Run `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact` and record result in `specs/026-artifact-envelope-and-types/quickstart.md`
- [x] T047 Run `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceArtifact` and record result in `specs/026-artifact-envelope-and-types/quickstart.md`
- [x] T048 Run `cd src/ElsaControl.Console && npm test -- --run artifacts` and record result in `specs/026-artifact-envelope-and-types/quickstart.md`
- [x] T049 Run `cd src/ElsaControl.Console && npm run typecheck` and record result in `specs/026-artifact-envelope-and-types/quickstart.md`
- [x] T050 Run `git diff --check` and record result in `specs/026-artifact-envelope-and-types/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1 and blocks all user stories.
- **US1 Submit Typed Artifact Envelope**: Depends on Phase 2; MVP.
- **US2 Manage Artifact Type Semantics**: Depends on Phase 2 and can proceed after built-in type registry exists.
- **US3 Preserve Safe Metadata Boundaries**: Depends on Phase 2 and strengthens US1 response/persistence behavior.
- **US4 Maintain Backward Compatibility**: Depends on Phase 2 and should complete before broad verification.
- **Phase 7 Polish**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 (P1)**: First independently deliverable slice.
- **US2 (P1)**: Can proceed after type registry and validation contracts exist.
- **US3 (P2)**: Can proceed after envelope metadata fields exist.
- **US4 (P3)**: Can proceed after envelope persistence model is known.

### Parallel Opportunities

- T001-T004 can run in parallel.
- T013-T015 can run in parallel after Phase 2.
- T020-T021 can run in parallel after Phase 2.
- T027-T029 can run in parallel after Phase 2.
- T034-T036 can run in parallel after Phase 2.
- T041-T043 can run in parallel during polish.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 typed envelope submission.
3. Validate through core, persistence, and API tests.

### Incremental Delivery

1. Add type discovery and console type/compatibility display.
2. Harden safe metadata validation and response shaping.
3. Add legacy projection and migration/backfill.
4. Run broad verification and update quickstart/contracts.
