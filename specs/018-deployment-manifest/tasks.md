# Tasks: Deployment Manifest Parsing

**Input**: Design documents from `specs/018-deployment-manifest/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`

**Tests**: Test tasks are included because this slice defines public manifest parsing and normalization behavior.

**Progress Tracking**: This file is the source of truth. Check tasks as they complete and add blocker notes directly under blocked tasks.

## Phase 1: Setup

**Purpose**: Add manifest source and test projects to the solution.

- [x] T001 Create manifest source project in `src/Elsa.Platform.Deployment.Manifest/Elsa.Platform.Deployment.Manifest.csproj`
- [x] T002 Create manifest test project in `tests/Elsa.Platform.Deployment.Manifest.Tests/Elsa.Platform.Deployment.Manifest.Tests.csproj`
- [x] T003 Add source and test projects to `Elsa.Platform.sln`
- [x] T004 Add required package versions to `Directory.Packages.props`
- [x] T005 Add source folders under `src/Elsa.Platform.Deployment.Manifest/`

---

## Phase 2: Foundational Manifest Types

**Purpose**: Define shared manifest result, metadata, and diagnostic helpers.

- [x] T006 [P] Create manifest constants in `src/Elsa.Platform.Deployment.Manifest/DeploymentManifestConstants.cs`
- [x] T007 [P] Create manifest metadata model in `src/Elsa.Platform.Deployment.Manifest/ManifestMetadata.cs`
- [x] T008 [P] Create manifest parse and normalization result models in `src/Elsa.Platform.Deployment.Manifest/ManifestParseResult.cs` and `src/Elsa.Platform.Deployment.Manifest/NormalizedManifest.cs`
- [x] T009 [P] Create manifest resource entry models in `src/Elsa.Platform.Deployment.Manifest/ManifestResourceEntries.cs`
- [x] T010 [P] Create manifest reader and normalizer interfaces in `src/Elsa.Platform.Deployment.Manifest/IManifestReader.cs` and `src/Elsa.Platform.Deployment.Manifest/IManifestNormalizer.cs`

---

## Phase 3: User Story 1 - Parse A Versioned Environment Manifest (Priority: P1)

**Goal**: Parse valid YAML/JSON manifests and normalize built-in resources.

**Independent Test**: A representative v1alpha manifest normalizes into deterministic deployment resources without artifact IO or engine execution.

### Tests

- [x] T011 [P] [US1] Add valid YAML and JSON reader tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestReaderTests.cs`
- [x] T012 [P] [US1] Add built-in normalization tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestNormalizationTests.cs`
- [x] T013 [P] [US1] Add deterministic hash equivalence tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestNormalizationTests.cs`

### Implementation

- [x] T014 [US1] Implement environment manifest model in `src/Elsa.Platform.Deployment.Manifest/EnvironmentManifest.cs`
- [x] T015 [US1] Implement YAML and JSON manifest reader in `src/Elsa.Platform.Deployment.Manifest/ManifestReader.cs`
- [x] T016 [US1] Implement built-in resource normalization in `src/Elsa.Platform.Deployment.Manifest/ManifestNormalizer.cs`
- [x] T017 [US1] Implement deterministic resource hash helper in `src/Elsa.Platform.Deployment.Manifest/ManifestResourceHasher.cs`

---

## Phase 4: User Story 2 - Report Manifest Diagnostics (Priority: P2)

**Goal**: Return structured diagnostics for malformed, unsupported, or invalid manifests.

**Independent Test**: Invalid manifests return expected diagnostic codes and severities without throwing parser exceptions.

### Tests

- [x] T018 [P] [US2] Add parse and schema diagnostic tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestDiagnosticTests.cs`
- [x] T019 [P] [US2] Add duplicate identity and path validation tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestDiagnosticTests.cs`

### Implementation

- [x] T020 [US2] Implement manifest validation diagnostics in `src/Elsa.Platform.Deployment.Manifest/ManifestValidator.cs`
- [x] T021 [US2] Implement duplicate identity detection in `src/Elsa.Platform.Deployment.Manifest/ManifestNormalizer.cs`
- [x] T022 [US2] Implement manifest-relative path validation in `src/Elsa.Platform.Deployment.Manifest/ManifestPathValidator.cs`

---

## Phase 5: User Story 3 - Preserve Forward-Compatible Manifest Shape (Priority: P3)

**Goal**: Preserve safe metadata and allow mapped custom resource sections while rejecting unmapped unknown sections.

**Independent Test**: Extension metadata is preserved, unmapped sections are rejected, and registered custom mappers normalize resources.

### Tests

- [x] T023 [P] [US3] Add extension metadata and custom mapper tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestExtensionTests.cs`

### Implementation

- [x] T024 [US3] Implement resource mapper interface in `src/Elsa.Platform.Deployment.Manifest/IManifestResourceMapper.cs`
- [x] T025 [US3] Implement resource mapper registry in `src/Elsa.Platform.Deployment.Manifest/ManifestResourceMapperRegistry.cs`
- [x] T026 [US3] Integrate unknown section diagnostics and registered custom mappings in `src/Elsa.Platform.Deployment.Manifest/ManifestNormalizer.cs`

---

## Phase 6: Boundaries And Verification

- [x] T027 [P] Add dependency boundary tests in `tests/Elsa.Platform.Deployment.Manifest.Tests/ManifestBoundaryTests.cs`
- [x] T028 Update deployment roadmap status in `docs/deployment-platform-phased-strategy.md`
- [x] T029 Update quickstart verification notes in `specs/018-deployment-manifest/quickstart.md`
- [x] T030 Run focused tests with `dotnet test tests/Elsa.Platform.Deployment.Manifest.Tests/Elsa.Platform.Deployment.Manifest.Tests.csproj`
- [x] T031 Run full solution tests with `dotnet test Elsa.Platform.sln`
- [x] T032 Run `git diff --check`
- [x] T033 Confirm `tasks.md` checkboxes and blocker notes reflect actual progress

---

## Dependencies & Execution Order

- Phase 1 setup blocks implementation.
- Phase 2 shared types block all user stories.
- User Story 1 is the MVP and must complete before diagnostics and extensions are meaningful.
- User Story 2 depends on the reader and normalizer.
- User Story 3 depends on the normalizer and diagnostics.

## Parallel Opportunities

- T006 through T010 can be implemented in parallel after setup.
- T011 through T013 can be written in parallel.
- T018 and T019 can be written in parallel.
- T023 and T027 can be written in parallel after normalization exists.

## MVP First

The MVP is setup plus User Story 1:

1. Manifest project and tests are in the solution.
2. Valid YAML and JSON manifests parse.
3. Built-in workflow, variable, feature, package, and recipe resources normalize deterministically.
