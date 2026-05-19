# Tasks: Platform Package Catalog Consolidation

**Input**: Design documents from `specs/001-platform-package-catalog-consolidation/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`

**Tests**: Test tasks are included because the migration must preserve behavior.

**Progress Tracking**: This file is the source of truth. Check tasks as they complete and add blocker notes directly under blocked tasks.

## Phase 1: Setup And Planning Baseline

**Purpose**: Establish Spec Kit and migration control documents.

- [x] T001 Initialize Spec Kit in repository root with `specify init --here --integration codex --force`
- [x] T002 Define platform constitution in `.specify/memory/constitution.md`
- [x] T003 Create consolidation feature spec in `specs/001-platform-package-catalog-consolidation/spec.md`
- [x] T004 Create implementation plan in `specs/001-platform-package-catalog-consolidation/plan.md`
- [x] T005 [P] Create research decisions in `specs/001-platform-package-catalog-consolidation/research.md`
- [x] T006 [P] Create data model in `specs/001-platform-package-catalog-consolidation/data-model.md`
- [x] T007 [P] Create dependency and migration contracts in `specs/001-platform-package-catalog-consolidation/contracts/`
- [x] T008 Create quickstart validation guide in `specs/001-platform-package-catalog-consolidation/quickstart.md`

**Checkpoint**: Planning baseline exists and can drive implementation.

---

## Phase 2: Foundational Import Preparation

**Purpose**: Prepare for a behavior-preserving repository import.

- [x] T009 Capture current `elsa-package-catalog` HEAD SHA and repository inventory in `specs/001-platform-package-catalog-consolidation/research.md`
- [x] T010 Decide history-preserving import mechanism and document command sequence in `specs/001-platform-package-catalog-consolidation/quickstart.md`
- [x] T011 Identify Spec Kit file conflicts between old repo and platform repo in `specs/001-platform-package-catalog-consolidation/research.md`
- [x] T012 Identify package ID compatibility status for `Elsa.PackageManifests` and `Elsa.PackageManifest.Generator` in `specs/001-platform-package-catalog-consolidation/research.md`
- [x] T013 Create ADR for package catalog consolidation under `docs/adr/`

**Checkpoint**: Import approach and compatibility assumptions are documented.

---

## Phase 3: User Story 1 - Establish Platform Home (Priority: P1)

**Goal**: Make the intended platform ownership and package catalog role explicit.

**Independent Test**: A maintainer can understand the target architecture from docs/specs without inspecting old conversation context.

- [x] T014 [US1] Update `docs/deployment-platform-phased-strategy.md` to reference Package Catalog as a platform sibling subsystem
- [x] T015 [US1] Add target platform repository structure to `README.md`
- [x] T016 [US1] Add catalog subsystem ownership notes to `specs/001-platform-package-catalog-consolidation/contracts/dependency-boundaries.md`
- [x] T017 [US1] Review and update `AGENTS.md` Spec Kit context pointer to this feature plan

**Checkpoint**: Platform home is documented.

---

## Phase 4: User Story 2 - Migrate Code Without Losing Behavior (Priority: P1)

**Goal**: Import catalog code into `elsa-platform` and prove current behavior still works.

**Independent Test**: Current catalog projects restore, build, and pass existing tests from `elsa-platform`.

### Tests

- [ ] T018 [P] [US2] Run baseline `dotnet test Elsa.PackageCatalog.sln` in old repo and record results in `specs/001-platform-package-catalog-consolidation/quickstart.md`
- [ ] T019 [P] [US2] Run baseline admin UI tests in old repo and record results in `specs/001-platform-package-catalog-consolidation/quickstart.md`

### Implementation

- [ ] T020 [US2] Import `elsa-package-catalog` into `elsa-platform` preserving git history if practical
- [ ] T021 [US2] Resolve path, solution, and Spec Kit file conflicts after import
- [ ] T022 [US2] Restore imported .NET projects from `elsa-platform`
- [ ] T023 [US2] Run imported .NET test suites from `elsa-platform`
- [ ] T024 [US2] Run imported admin UI unit tests from `elsa-platform`
- [ ] T025 [US2] Document any pre-existing or migration-caused test failures in `specs/001-platform-package-catalog-consolidation/quickstart.md`

**Checkpoint**: Current catalog behavior is preserved in the platform repository.

---

## Phase 5: User Story 3 - Normalize Package Architecture (Priority: P2)

**Goal**: Move from imported catalog names to ideal platform subsystem names and boundaries.

**Independent Test**: Renamed projects build and tests pass with target dependency direction.

### Tests

- [ ] T026 [P] [US3] Add or update manifest contract tests for renamed `Elsa.Platform.PackageManifests`
- [ ] T027 [P] [US3] Add dependency boundary inspection notes to `specs/001-platform-package-catalog-consolidation/contracts/dependency-boundaries.md`

### Implementation

- [ ] T028 [US3] Rename `Elsa.Catalog.Core` to `Elsa.Platform.PackageCatalog.Core`
- [ ] T029 [US3] Extract `Elsa.Platform.PackageCatalog.Abstractions` from catalog core contracts
- [ ] T030 [US3] Rename `Elsa.Catalog.Packaging.NuGet` to `Elsa.Platform.PackageCatalog.Sources.NuGet`
- [ ] T031 [US3] Rename catalog API, AppHost, ServiceDefaults, AdminUi, persistence, and migration projects to `Elsa.Platform.PackageCatalog.*`
- [ ] T032 [US3] Rename `Elsa.PackageManifests` source project to `Elsa.Platform.PackageManifests`
- [ ] T033 [US3] Rename generator projects to `Elsa.Platform.PackageManifest.Generator*`
- [ ] T034 [US3] Update namespaces, project references, solution files, test project references, package metadata, and docs after project renames
- [ ] T035 [US3] Run .NET restore/build/test after renames
- [ ] T036 [US3] Run admin UI tests after path and package updates

**Checkpoint**: Platform package architecture is in place and verified.

---

## Phase 6: User Story 4 - Deprecate Old Repository Safely (Priority: P2)

**Goal**: Make `elsa-platform` the source of truth and retire active work in `elsa-package-catalog`.

**Independent Test**: Old repo points to `elsa-platform` and has no untriaged open work.

- [ ] T037 [US4] Inventory open issues and PRs in `elsa-workflows/elsa-package-catalog`
- [ ] T038 [US4] Migrate, link, or close old repo issues/specs with rationale
- [ ] T039 [US4] Update old repo README to mark repository deprecated and link to `elsa-platform`
- [ ] T040 [US4] Decide archive timing and document it in `specs/001-platform-package-catalog-consolidation/research.md`

**Checkpoint**: Old repository is deprecated or ready to archive.

---

## Phase 7: User Story 5 - Enable Deployment Integration (Priority: P3)

**Goal**: Prepare package catalog contracts for deployment package descriptor validation.

**Independent Test**: Deployment packages can consume catalog abstractions/client contracts without referencing catalog internals.

### Tests

- [ ] T041 [P] [US5] Add dependency boundary check showing Deployment does not reference catalog API/UI/persistence projects
- [ ] T042 [P] [US5] Add contract tests for package requirement validation result shape

### Implementation

- [ ] T043 [US5] Define package lookup and compatibility contracts in `src/Elsa.Platform.PackageCatalog.Abstractions/`
- [ ] T044 [US5] Add deployment-facing catalog client or adapter contract without referencing catalog internals
- [ ] T045 [US5] Update deployment phased strategy to describe catalog-backed package descriptor validation
- [ ] T046 [US5] Verify package validity, approval, trust, suspicious, and compatibility remain distinct in the contract

**Checkpoint**: Deployment integration path is ready for Deployment Phase 1 package descriptors.

---

## Final Phase: Polish And Release Readiness

- [ ] T047 Run full .NET test suite from `elsa-platform`
- [ ] T048 Run relevant UI test suites from `elsa-platform`
- [ ] T049 Review docs for old names and update remaining references
- [ ] T050 Add or update ADRs for package identity compatibility and repository deprecation
- [ ] T051 Confirm `tasks.md` checkboxes and blocker notes reflect actual progress

---

## Dependencies & Execution Order

- Phase 1 must be complete before any implementation migration.
- Phase 2 blocks import work.
- Phase 3 can proceed before code import but should be completed before renaming.
- Phase 4 must complete before Phase 5 architecture cleanup.
- Phase 5 must complete before Deployment integration in Phase 7.
- Phase 6 can start after Phase 4 but should not archive the old repo until Phase 5 is stable.

## Parallel Opportunities

- T009 through T012 can be researched in parallel.
- Baseline old-repo tests T018 and T019 can run in parallel.
- Rename tasks should be staged carefully, but test additions and dependency-boundary docs can happen in parallel.
- Old repository issue inventory can happen in parallel with architecture cleanup after import is stable.

## MVP First

The MVP is Phase 1 through Phase 4:

1. Spec Kit initialized.
2. Migration plan and tasks exist.
3. Current catalog code imported into `elsa-platform`.
4. Existing catalog behavior verified from the platform repo.

Stop and validate after Phase 4 before package renaming.
