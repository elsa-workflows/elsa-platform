# Tasks: Deployment Artifact Packaging

**Input**: Design documents from `specs/019-deployment-artifacts/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Test tasks are included because this slice has explicit measurable verification outcomes and boundary guarantees.

**Organization**: Tasks are grouped by user story to enable independently testable increments.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the artifact package and test project.

- [x] T001 Create `src/ValenceControl.Deployment.Artifacts/ValenceControl.Deployment.Artifacts.csproj` referencing `src/ValenceControl.Deployment.Abstractions/ValenceControl.Deployment.Abstractions.csproj` and `src/ValenceControl.Deployment.Manifest/ValenceControl.Deployment.Manifest.csproj`
- [x] T002 Create `tests/ValenceControl.Deployment.Artifacts.Tests/ValenceControl.Deployment.Artifacts.Tests.csproj` with artifact, manifest, abstractions, and xUnit references; use xUnit's built-in assertions.
- [x] T003 Add artifact source and test projects to `ValenceControl.sln`
- [x] T004 [P] Create initial namespace placeholder files in `src/ValenceControl.Deployment.Artifacts/ArtifactLayoutConstants.cs` and `src/ValenceControl.Deployment.Artifacts/ArtifactDiagnosticCodes.cs`
- [x] T005 [P] Create initial test fixture file `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactTestFixtures.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared contracts and helpers required by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T006 Create artifact format, entry kind, and checksum status models in `src/ValenceControl.Deployment.Artifacts/ArtifactTypes.cs`
- [x] T007 Create artifact metadata and checksum records in `src/ValenceControl.Deployment.Artifacts/ArtifactMetadata.cs`
- [x] T008 Create build and inspection result records in `src/ValenceControl.Deployment.Artifacts/ArtifactResults.cs`
- [x] T009 Create build option records in `src/ValenceControl.Deployment.Artifacts/ArtifactBuildOptions.cs`
- [x] T010 Create public builder and reader interfaces in `src/ValenceControl.Deployment.Artifacts/IDeploymentArtifactBuilder.cs` and `src/ValenceControl.Deployment.Artifacts/IDeploymentArtifactReader.cs`
- [x] T011 Implement path normalization and traversal rejection in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactPathValidator.cs`
- [x] T012 Implement SHA-256 checksum helpers in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactChecksumService.cs`
- [x] T013 [P] Add path validation tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactPathValidationTests.cs`
- [x] T014 [P] Add checksum helper tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactChecksumTests.cs`

**Checkpoint**: Foundation ready; user story implementation can start.

---

## Phase 3: User Story 1 - Build Folder Artifact (Priority: P1) MVP

**Goal**: Build deterministic, atomic folder artifacts from a manifest and workspace root.

**Independent Test**: Build a folder artifact from fixture files and verify metadata, manifest snapshot, payload files, checksum inventory, stable identity, and diagnostics for invalid input.

### Tests for User Story 1

- [x] T015 [P] [US1] Add successful folder build test in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs`
- [x] T016 [P] [US1] Add deterministic folder artifact identity test in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs`
- [x] T017 [P] [US1] Add missing payload, duplicate path, and traversal diagnostics tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs`
- [x] T018 [P] [US1] Add atomic failed-build cleanup test in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs`

### Implementation for User Story 1

- [x] T019 [US1] Implement folder artifact builder skeleton in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`
- [x] T020 [US1] Implement manifest snapshot writing in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`
- [x] T021 [US1] Implement payload collection from normalized manifest resource metadata in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`
- [x] T022 [US1] Implement artifact metadata creation in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`
- [x] T023 [US1] Implement checksum inventory writing in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`
- [x] T024 [US1] Implement staged folder output and atomic publish behavior in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`

**Checkpoint**: Folder artifact build is fully functional and testable independently.

---

## Phase 4: User Story 2 - Read And Inspect Artifact (Priority: P2)

**Goal**: Read folder and ZIP artifacts, inspect metadata/resources/checksums, and report verification diagnostics.

**Independent Test**: Load known artifacts and verify inspection results without a deployment engine or target.

### Tests for User Story 2

- [x] T025 [P] [US2] Add successful folder inspection test in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactReaderTests.cs`
- [x] T026 [P] [US2] Add checksum mismatch, missing file, and unexpected file inspection tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactReaderTests.cs`
- [x] T027 [P] [US2] Add missing metadata, missing manifest, and unsupported layout tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactReaderTests.cs`
- [x] T028 [P] [US2] Add ZIP/folder logical parity test in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactReaderTests.cs`
- [x] T029 [P] [US2] Add archive traversal rejection test in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactReaderTests.cs`

### Implementation for User Story 2

- [x] T030 [US2] Implement folder artifact reader skeleton in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactReader.cs`
- [x] T031 [US2] Implement metadata, manifest, entry, and checksum inventory parsing in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactReader.cs`
- [x] T032 [US2] Implement checksum verification and diagnostic mapping in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactReader.cs`
- [x] T033 [US2] Implement ZIP artifact writing in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactBuilder.cs`
- [x] T034 [US2] Implement ZIP artifact reading with archive path validation in `src/ValenceControl.Deployment.Artifacts/DeploymentArtifactReader.cs`

**Checkpoint**: Folder and ZIP artifacts can be inspected and checksum-verified.

---

## Phase 5: User Story 3 - Preserve Artifact Boundaries (Priority: P3)

**Goal**: Ensure the artifact package remains portable, extensible, and separated from engine, hosting, transport, and deferred enterprise features.

**Independent Test**: Boundary tests verify allowed references and forbidden behavioral scope.

### Tests for User Story 3

- [x] T035 [P] [US3] Add project reference boundary tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBoundaryTests.cs`
- [x] T036 [P] [US3] Add source namespace boundary tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBoundaryTests.cs`
- [x] T037 [P] [US3] Add public contract shape tests in `tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBoundaryTests.cs`

### Implementation for User Story 3

- [x] T038 [US3] Adjust artifact public contracts to avoid engine, CLI, API, hosting, persistence, OCI, signing, policy, and runtime-state types in `src/ValenceControl.Deployment.Artifacts/`
- [x] T039 [US3] Ensure artifact diagnostics use deployment abstraction diagnostics in `src/ValenceControl.Deployment.Artifacts/ArtifactDiagnosticCodes.cs`

**Checkpoint**: Artifact package boundary is enforced and documented.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Align docs and verify the full slice.

- [x] T040 [P] Update `specs/019-deployment-artifacts/quickstart.md` if implementation paths or method names changed
- [x] T041 [P] Update `specs/019-deployment-artifacts/contracts/artifact-layout.md` to match final JSON field names
- [x] T042 Run `dotnet test tests/ValenceControl.Deployment.Artifacts.Tests/ValenceControl.Deployment.Artifacts.Tests.csproj`
- [x] T043 Run full solution `dotnet test`
- [x] T044 Run `git diff --check`
- [x] T045 Update task checkboxes in `specs/019-deployment-artifacts/tasks.md` as completed

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational and is the MVP.
- **User Story 2 (Phase 4)**: Depends on User Story 1 because it needs readable artifacts to inspect.
- **User Story 3 (Phase 5)**: Depends on public contract shape from User Stories 1 and 2.
- **Polish (Phase 6)**: Depends on selected user stories being complete.

### Parallel Opportunities

- T004 and T005 can run in parallel after project files exist.
- T013 and T014 can run in parallel after T006-T012 contracts are sketched.
- T015-T018 can be written in parallel before T019-T024 implementation.
- T025-T029 can be written in parallel before T030-T034 implementation.
- T035-T037 can be written in parallel after public contract files exist.
- T040 and T041 can run in parallel during polish.

## Parallel Example: User Story 1

```text
Task: "Add successful folder build test in tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs"
Task: "Add deterministic folder artifact identity test in tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs"
Task: "Add missing payload, duplicate path, and traversal diagnostics tests in tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs"
Task: "Add atomic failed-build cleanup test in tests/ValenceControl.Deployment.Artifacts.Tests/ArtifactBuilderTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational tasks.
2. Complete User Story 1.
3. Verify folder artifact build, deterministic identity, and atomic failure behavior.
4. Stop and validate before adding readers and ZIP parity.

### Incremental Delivery

1. Folder artifact build creates the first useful artifact.
2. Folder/ZIP inspection makes artifacts reusable by future CLI/API/engine slices.
3. Boundary tests preserve portability before the package becomes a shared dependency.
