# Tasks: Runtime Kind Compatibility

**Input**: Design documents from `/specs/032-runtime-kind-compatibility/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/runtime-kind-compatibility.md

**Tests**: Included because the feature specification defines independent tests for each user story and the project constitution requires incremental verifiability.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish shared runtime-kind constants and locate affected projections.

- [x] T001 Add official runtime-kind constants and normalization helpers in `src/Elsa.Platform.PackageManifests/Compatibility/RuntimeKindCompatibility.cs`
- [x] T002 [P] Add runtime-kind sample manifests in `src/Elsa.Platform.PackageManifests/Schemas/examples/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extend manifest contracts and validation before catalog/API/UI consumers depend on runtime-kind metadata.

- [x] T003 Add package-level runtime kind support to `src/Elsa.Platform.PackageManifests/Compatibility/CompatibilityManifest.cs`
- [x] T004 Add feature-level compatibility support to `src/Elsa.Platform.PackageManifests/FeatureManifest.cs`
- [x] T005 Update schema contract for package and feature runtime kinds in `src/Elsa.Platform.PackageManifests/Schemas/elsa-package-manifest.v1.json`
- [x] T006 Add runtime-kind validation to `src/Elsa.Platform.PackageManifests/Validation/ManifestValidator.cs`
- [x] T007 [P] Add manifest contract and validation tests in `tests/Elsa.Platform.PackageManifests.Tests/ManifestRuntimeKindCompatibilityTests.cs`
- [x] T008 Update generator override models to accept feature and package runtime kind declarations in `src/Elsa.Platform.PackageManifest.Generator.Core/Overrides/ManifestOverrideModels.cs`
- [x] T009 [P] Add generator override validation tests in `tests/Elsa.Platform.PackageManifest.Generator.Core.Tests/FeatureDiscoveryTests.cs`

**Checkpoint**: Manifest contracts can express and validate runtime-kind compatibility.

---

## Phase 3: User Story 1 - Filter Packages By Target Runtime (Priority: P1) MVP

**Goal**: Elsa Server package experiences select only server-compatible packages and features while excluding Studio-only metadata.

**Independent Test**: Publish or seed server-only, studio-only, and mixed package metadata, then verify Elsa Server catalog/builder output includes only compatible packages and features.

### Tests for User Story 1

- [x] T010 [P] [US1] Add catalog projection tests for server/studio/mixed package filtering in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs`
- [x] T011 [P] [US1] Add Runtime Builder catalog tests for excluding studio-only features in `tests/Elsa.Platform.Api.Tests/PublicBuilderApiTests.cs`
- [x] T012 [P] [US1] Add API projection tests for runtime kind metadata in `tests/Elsa.Platform.Api.Tests/PublicPackagesApiTests.cs`

### Implementation for User Story 1

- [x] T013 [US1] Extend catalog feature/package models with effective runtime kinds in `src/Elsa.Platform.PackageCatalog.Abstractions/Catalog/PublicCatalogContracts.cs`
- [x] T014 [US1] Persist package and feature runtime kinds through manifest ingestion in `src/Elsa.Platform.PackageCatalog.Core/Manifests/ManifestIngestionService.cs`
- [x] T015 [US1] Preserve runtime kind metadata through existing stored manifest JSON in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs`
- [x] T016 [US1] Project effective runtime kinds from catalog queries in `src/Elsa.Platform.PackageCatalog.Core/Packages/PublicCatalogQueryService.cs` and `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs`
- [x] T017 [US1] Expose runtime kind metadata through public package/feature API contracts in `src/Elsa.Platform.Api/Public/Packages/PublicPackageContracts.cs` and `src/Elsa.Platform.Api/Public/Features/PublicFeatureContracts.cs`
- [x] T018 [US1] Filter Elsa Server builder package/features by `elsa.server` in `src/Elsa.Platform.Api/Public/Builder/BuilderEndpoints.cs` and `src/Elsa.Platform.Api/Workspace/WorkspaceBuilderEndpoints.cs`
- [x] T019 [US1] Update console runtime-builder models to carry runtime kind metadata in `src/Elsa.Platform.Console/src/features/runtime-builder/runtimeBuilderModels.ts`

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Declare Runtime Applicability In Manifests (Priority: P2)

**Goal**: Package authors can declare official and custom runtime kinds at package or feature level, and catalog consumers receive those declarations after inheritance/override resolution.

**Independent Test**: Validate manifests with official and custom runtime-kind values, ingest them, and confirm the catalog preserves and exposes declared values.

### Tests for User Story 2

- [x] T020 [P] [US2] Add manifest serialization round-trip tests for package and feature compatibility in `tests/Elsa.Platform.PackageManifests.Tests/ManifestSerializationTests.cs`
- [x] T021 [P] [US2] Add manifest ingestion tests for package defaults and feature overrides in `tests/Elsa.Platform.PackageCatalog.Core.Tests/ManifestIngestionServiceTests.cs`
- [x] T022 [P] [US2] Add persistence tests for preserving unknown valid runtime kinds through stored manifest JSON in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs`

### Implementation for User Story 2

- [x] T023 [US2] Implement effective runtime-kind resolution helpers in `src/Elsa.Platform.PackageCatalog.Core/Compatibility/RuntimeKindCompatibilityPolicy.cs`
- [x] T024 [US2] Apply package-default and feature-override resolution during ingestion in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs`
- [x] T025 [US2] Preserve unknown valid runtime kinds in public feature/package projections in `src/Elsa.Platform.PackageCatalog.Abstractions/Catalog/PublicCatalogContracts.cs`

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Preserve Existing Package Behavior (Priority: P3)

**Goal**: Existing manifests without runtime-kind declarations continue to appear in Elsa Server experiences and do not appear as Studio-compatible by default.

**Independent Test**: Sync an existing undeclared manifest and verify effective Elsa Server compatibility only.

### Tests for User Story 3

- [x] T026 [P] [US3] Add defaulting tests for undeclared manifests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/ManifestIngestionServiceTests.cs`
- [x] T027 [P] [US3] Add API tests proving undeclared packages project `elsa.server` compatibility in `tests/Elsa.Platform.Api.Tests/PublicPackageVersionApiTests.cs`

### Implementation for User Story 3

- [x] T028 [US3] Add backward-compatible server defaulting in `src/Elsa.Platform.PackageCatalog.Core/Compatibility/RuntimeKindCompatibilityPolicy.cs`
- [x] T029 [US3] Ensure undeclared packages remain visible in existing package and Runtime Builder flows in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs` and `src/Elsa.Platform.Api/Public/Builder/BuilderEndpoints.cs`

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, alignment, and verification.

- [x] T030 [P] Update manifest author documentation in `src/Elsa.Platform.PackageManifests/README.md`
- [x] T031 [P] Update generator documentation in `src/Elsa.Platform.PackageManifest.Generator/README.md`
- [x] T032 [P] Update package catalog test fixtures in `tests/Elsa.Platform.PackageCatalog.Testing/ManifestFixtureBuilder.cs`
- [x] T033 Run quickstart validation commands from `specs/032-runtime-kind-compatibility/quickstart.md`
- [x] T034 Run self-review against `specs/032-runtime-kind-compatibility/spec.md`, `plan.md`, and `tasks.md` and fix any high-priority findings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational phase completion.
- **User Story 2 (Phase 4)**: Depends on Foundational phase completion and integrates with US1 projection paths.
- **User Story 3 (Phase 5)**: Depends on Foundational phase completion and shares defaulting policy with US2.
- **Polish (Phase 6)**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1**: Can start after Foundational; delivers MVP filtering for Elsa Server consumers.
- **US2**: Can start after Foundational; depends on shared projection shape from US1 for full catalog exposure.
- **US3**: Can start after Foundational; depends on the shared runtime-kind policy introduced for US2.

### Parallel Opportunities

- T002 can run in parallel with T001.
- T007 and T009 can run in parallel after T003-T006 are drafted.
- T010, T011, and T012 can be drafted in parallel.
- T020, T021, and T022 can be drafted in parallel.
- T026 and T027 can be drafted in parallel.
- T030, T031, and T032 can run in parallel.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational manifest contract tasks.
2. Complete US1 to make Runtime Builder/catalog filtering correct for Elsa Server.
3. Run the US1-focused test set before continuing.

### Incremental Delivery

1. Add manifest contract and validation.
2. Add catalog/API projection and Runtime Builder filtering.
3. Add author-facing declaration preservation and custom runtime-kind behavior.
4. Add backward-compatible undeclared-manifest behavior.
5. Update documentation and run the quickstart validation commands.
