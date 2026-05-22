# Tasks: Package Details Page

**Input**: Design documents from `/specs/006-package-details-page/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/admin-package-details.md, quickstart.md

**Tests**: Tests are included because the specification requires acceptance coverage for page states and the quickstart defines API/UI verification.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared package-details fixtures, query keys, and route placeholders used across stories.

- [X] T001 Add package details fixture data with multiple versions, source summary, visibility blockers, compatibility metadata, version state tokens, features, settings, validation findings, empty-version package data, and manifest content in `src/Elsa.Platform.Console/src/test/fixtures.ts`
- [X] T002 [P] Add package details request handlers for package, validation, manifest, approval, and rejection responses in `src/Elsa.Platform.Console/src/test/adminApiHandlers.ts`
- [X] T003 [P] Add Package Details page test scaffold and render helper in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx`
- [X] T004 [P] Add API seed helper methods for multi-version package details, feature settings, suspicious hashes, and validation payloads in `tests/Elsa.Platform.PackageCatalog.Testing/PublicCatalogSeedData.cs`
- [X] T005 Add package details query keys for package, validation, and manifest resources in `src/Elsa.Platform.Console/src/lib/query/queryClient.tsx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared backend contracts, projection helpers, and frontend models before story-specific UI work.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 [P] Expand admin package details response records for source summary, version details, version state token, compatibility metadata, visibility reasons, feature records, settings, and manifest metadata in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminPackageContracts.cs`
- [X] T007 [P] Add TypeScript package details, compatibility metadata, visibility reason, validation finding, feature, setting, manifest, version state token, and version action types in `src/Elsa.Platform.Console/src/features/packages/packageModels.ts`
- [X] T008 [P] Add package details API client functions for details, validation, manifest, approve, and reject calls in `src/Elsa.Platform.Console/src/features/packages/packageApi.ts`
- [X] T009 Add visibility reason projection helper covering approval, rejection, validation, listing, suspicious, source, manifest, and ingestion reasons in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [X] T010 Add validation JSON normalization helper for admin findings in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminValidationEndpoints.cs`
- [X] T011 Add frontend model helpers for selected version resolution, route section parsing, visibility grouping, version state token comparison, and stale action detection in `src/Elsa.Platform.Console/src/features/packages/packageModels.ts`
- [X] T012 [P] Add unit tests for selected latest indexed version, canonical casing display, visibility grouping, section parsing, compatibility filtering, and version state token stale action helper behavior in `src/Elsa.Platform.Console/src/features/packages/packageModels.test.ts`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Inspect Package Summary (Priority: P1) MVP

**Goal**: An administrator opens package details and immediately sees identity, selected version, source, status, visibility, and not-found/casing behavior.

**Independent Test**: Open a known package details route for a package with multiple versions and verify summary, latest indexed default version, source, timestamps, visibility reasons, not-found state, and canonical casing.

### Tests for User Story 1

- [X] T013 [P] [US1] Add admin API tests for package details summary, source summary, canonical casing, latest indexed default data, no-versions package data, and not-found behavior in `tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminPackagesApiTests.cs`
- [X] T014 [P] [US1] Add Package Details UI tests for summary rendering, latest indexed default selection, visibility reasons, canonical casing, no-versions empty state, access-denied stale data clearing, and not-found state in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx`

### Implementation for User Story 1

- [X] T015 [US1] Update admin package details endpoint to load source, all versions, feature counts, manifest metadata, version state tokens, canonical casing, no-versions responses, and visibility reasons in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [X] T016 [US1] Update EF Core admin package lookup to include source, versions, features, and settings with case-insensitive package ID matching in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/ApprovalStore.cs`
- [X] T017 [US1] Implement Package Details page shell with summary, source panel, status badges, visibility reasons, loading, no-versions, not-found, access-denied stale data clearing, and unexpected-error states in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T018 [US1] Wire package details route to replace placeholder package routes in `src/Elsa.Platform.Console/src/app/routes.tsx`
- [X] T019 [US1] Update packages list links to preserve canonical package ID route encoding in `src/Elsa.Platform.Console/src/features/packages/PackagesPage.tsx`

**Checkpoint**: User Story 1 is independently usable as the MVP package details page.

---

## Phase 4: User Story 2 - Compare and Select Versions (Priority: P1)

**Goal**: Administrators can inspect available versions, switch selected version, and restore version/section state from direct links.

**Independent Test**: Seed approved, pending, rejected, invalid, unlisted, and suspicious versions; switch versions and confirm every version-scoped section updates consistently, including direct version and section links.

### Tests for User Story 2

- [X] T020 [P] [US2] Add UI tests for version list badges, version switching, missing version recovery, and version-plus-section deep links in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx`
- [X] T021 [P] [US2] Add E2E smoke test for package details route, version route, and section route navigation in `tests/Elsa.Platform.Console.E2E/package-details.spec.ts`

### Implementation for User Story 2

- [X] T022 [US2] Implement version selector/list with approval, validation, listed, suspicious, and indexed timestamp display in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T023 [US2] Implement URL synchronization for package-level default version, explicit version routes, and major section routes in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T024 [US2] Add recoverable version-not-found and unavailable section states in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T025 [US2] Update route definitions for `/admin/packages/:packageId/versions/:version` and `/admin/packages/:packageId/versions/:version/:section` in `src/Elsa.Platform.Console/src/app/routes.tsx`

**Checkpoint**: User Story 2 works independently with direct links and version switching.

---

## Phase 5: User Story 3 - Diagnose Validation and Visibility (Priority: P1)

**Goal**: Support engineers can inspect validation findings, suspicious manifest evidence, and grouped visibility blockers for the selected version.

**Independent Test**: Open details for versions with no findings, warnings, errors, suspicious hash changes, and multiple blockers; verify each case is understandable and scoped failures do not collapse the whole page.

### Tests for User Story 3

- [X] T026 [P] [US3] Add admin API tests for normalized validation findings with errors, warnings, missing code/path, no findings, and package/version not found in `tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminValidationApiTests.cs`
- [X] T027 [P] [US3] Add UI tests for validation grouping, valid empty state, suspicious hash evidence, multiple blocker groups, and validation-load failure state in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx`

### Implementation for User Story 3

- [X] T028 [US3] Update admin validation endpoint to return normalized `findings` with severity, code, message, path, blocking impact, validated timestamp, and validator version in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminValidationEndpoints.cs`
- [X] T029 [US3] Add validation findings API response types and normalization for historic result payload shapes in `src/Elsa.Platform.Console/src/features/packages/packageModels.ts`
- [X] T030 [US3] Implement validation and visibility sections with grouped blockers, valid state, suspicious hash evidence, and scoped validation failure state in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T031 [US3] Add validation and visibility search/filter helpers for severity, blocking state, code, path, and message in `src/Elsa.Platform.Console/src/features/packages/packageModels.ts`

**Checkpoint**: User Story 3 supports independent troubleshooting for validation and visibility.

---

## Phase 6: User Story 4 - Inspect Features, Settings, Dependencies, and Compatibility (Priority: P2)

**Goal**: Administrators can inspect features, settings, dependencies, conflicts, infrastructure, and compatibility metadata for the selected version.

**Independent Test**: Open a rich package and a package with no indexed feature data; verify readable rich and empty states plus in-page search/filtering across large lists.

### Tests for User Story 4

- [X] T032 [P] [US4] Add admin API tests for feature, setting, dependency, conflict, infrastructure, compatibility metadata, and empty feature projections in `tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminPackagesApiTests.cs`
- [X] T033 [P] [US4] Add UI tests for features, settings, dependencies, conflicts, compatibility metadata, empty states, seeded large-list scenarios, and in-page filters in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx`

### Implementation for User Story 4

- [X] T034 [US4] Extend admin package details projection to include compatibility metadata plus feature settings and JSON-backed dependency, conflict, infrastructure, validation, UI, and extension fields in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [X] T035 [US4] Add feature, setting, dependency, conflict, infrastructure, compatibility metadata, and large-list filter helpers in `src/Elsa.Platform.Console/src/features/packages/packageModels.ts`
- [X] T036 [US4] Implement feature and settings inspection UI with compact rows, expandable details, badges, and empty states in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T037 [US4] Implement dependencies, conflicts, infrastructure, and compatibility sections with search/filter controls in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`

**Checkpoint**: User Story 4 gives administrators an inspectable functional surface for the selected version.

---

## Phase 7: User Story 5 - Review Manifest Content and Version Actions (Priority: P2)

**Goal**: Administrators can inspect manifest metadata/raw content and perform available version-scoped actions through deliberate confirmation flows.

**Independent Test**: Open versions with available and missing manifest content, approve a pending version, reject with a reason, block blank rejection, and verify stale/unsupported action states.

### Tests for User Story 5

- [X] T038 [P] [US5] Add admin API tests for manifest metadata/content availability, approval success, rejection reason validation, stale `expectedStateToken` conflicts, and version not found behavior in `tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminPackagesApiTests.cs`
- [X] T039 [P] [US5] Add admin approval API tests for blank rejection reason rejection, version state token mismatch rejection, and version-scoped approve/reject behavior in `tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminApprovalApiTests.cs`
- [X] T040 [P] [US5] Add UI tests for manifest viewer, manifest search, approve confirmation, reject reason requirement, version state token stale action block, conflict refresh prompt, and unsupported action display in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx`

### Implementation for User Story 5

- [X] T041 [US5] Include manifest availability, schema version, stored hash, suspicious hash, version state token inputs, and raw manifest content in admin package details responses in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [X] T042 [US5] Enforce non-empty version rejection reasons, require matching `expectedStateToken`, return conflict on stale state, and preserve version-scoped approval/rejection behavior in `src/Elsa.Platform.PackageCatalog.Api/Admin/Packages/AdminApprovalEndpoints.cs`
- [X] T043 [US5] Add manifest search, formatting, and availability helpers in `src/Elsa.Platform.Console/src/features/packages/packageModels.ts`
- [X] T044 [US5] Implement read-only manifest section with metadata, formatted content, search/jump support, and missing/malformed states in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T045 [US5] Implement version action panel with approve/reject confirmations, rejection reason requirement, unavailable optional actions, version state token stale action block, conflict refresh prompt, success refresh, and failure preservation in `src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.tsx`
- [X] T046 [US5] Update package action API client to send `expectedStateToken` and surface conflict, validation, not-found, access, and unexpected failures to the page in `src/Elsa.Platform.Console/src/features/packages/packageApi.ts`

**Checkpoint**: User Story 5 completes manifest review and version-scoped actions.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation alignment, and quality cleanup across all stories.

- [X] T047 [P] Update console package details documentation and verification notes in `src/Elsa.Platform.Console/README.md`
- [X] T048 [P] Update package details quickstart learnings after implementation in `specs/006-package-details-page/quickstart.md`
- [X] T049 Run API regression tests from quickstart and record any command adjustments in `specs/006-package-details-page/quickstart.md`
- [X] T050 Run console unit tests, seeded large-list inspection checks, and Playwright package details smoke test from quickstart, then record any command adjustments in `specs/006-package-details-page/quickstart.md`
- [X] T051 Review package details implementation for no new durable storage, no public API changes, no manifest schema changes, and no package code execution in `specs/006-package-details-page/plan.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion - blocks all user stories.
- **User Stories (Phase 3+)**: Depend on Foundational completion. P1 stories should be completed before P2 stories for MVP value.
- **Polish (Phase 8)**: Depends on desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Starts after Foundation; no dependency on other stories. Suggested MVP.
- **User Story 2 (P1)**: Starts after Foundation; uses Package Details page shell from US1 for final integration.
- **User Story 3 (P1)**: Starts after Foundation; can run after or alongside US2 once details data is available.
- **User Story 4 (P2)**: Starts after Foundation; easiest after US2 section routing exists.
- **User Story 5 (P2)**: Starts after Foundation; action panel benefits from US2 selected-version routing and US3 visibility context.

### Within Each User Story

- Write tests first and confirm they fail before implementation.
- Backend contracts/projections before UI data integration.
- Model helpers before page rendering for the same section.
- Route or API client updates before page tests that rely on them.
- Validate each story at its checkpoint before moving to the next story.

---

## Parallel Opportunities

- T002, T003, and T004 can run in parallel after T001 is understood.
- T006, T007, T008, and T012 can run in parallel during Foundation.
- T013 and T014 can run in parallel for US1.
- T020 and T021 can run in parallel for US2.
- T026 and T027 can run in parallel for US3.
- T032 and T033 can run in parallel for US4.
- T038, T039, and T040 can run in parallel for US5.
- US3 backend validation work can proceed while US2 route work is underway, because they touch different files.
- US4 feature projection work can proceed while US5 action tests are being written, because the write sets are mostly separate.

---

## Parallel Example: User Story 1

```text
Task: "T013 [P] [US1] Add admin API tests for package details summary, source summary, canonical casing, latest indexed default data, and not-found behavior in tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminPackagesApiTests.cs"
Task: "T014 [P] [US1] Add Package Details UI tests for summary rendering, latest indexed default selection, visibility reasons, canonical casing, and not-found state in src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx"
```

## Parallel Example: User Story 3

```text
Task: "T026 [P] [US3] Add admin API tests for normalized validation findings with errors, warnings, missing code/path, no findings, and package/version not found in tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminValidationApiTests.cs"
Task: "T027 [P] [US3] Add UI tests for validation grouping, valid empty state, suspicious hash evidence, multiple blocker groups, and validation-load failure state in src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx"
```

## Parallel Example: User Story 5

```text
Task: "T038 [P] [US5] Add admin API tests for manifest metadata/content availability, approval success, rejection reason validation, stale expectedStateToken conflicts, and version not found behavior in tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminPackagesApiTests.cs"
Task: "T039 [P] [US5] Add admin approval API tests for blank rejection reason rejection, version state token mismatch rejection, and version-scoped approve/reject behavior in tests/Elsa.Platform.PackageCatalog.Api.Tests/AdminApprovalApiTests.cs"
Task: "T040 [P] [US5] Add UI tests for manifest viewer, manifest search, approve confirmation, reject reason requirement, version state token stale action block, conflict refresh prompt, and unsupported action display in src/Elsa.Platform.Console/src/features/packages/PackageDetailsPage.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Stop and validate package summary, canonical casing, default version, visibility reasons, and not-found behavior independently.
5. Demo `/admin/packages/:packageId` replacing the placeholder with useful read-only details.

### Incremental Delivery

1. Add US1 summary MVP and validate.
2. Add US2 version switching and deep links.
3. Add US3 validation and visibility diagnostics.
4. Add US4 feature/settings/dependency/compatibility inspection.
5. Add US5 manifest review and actions.
6. Run quickstart verification and polish.

### Parallel Team Strategy

With multiple developers:

1. Complete Setup and Foundation together.
2. Developer A implements US1 page shell and backend projection.
3. Developer B prepares US3 validation normalization and tests.
4. Developer C prepares US4 feature/settings inspection tests and helpers.
5. Integrate stories through shared `PackageDetailsPage.tsx` carefully because most UI rendering converges there.

---

## Notes

- [P] tasks touch different files or can be completed without depending on incomplete task output.
- [US1]-[US5] labels map directly to the user stories in `spec.md`.
- Tests are intentionally included because acceptance coverage is part of the feature success criteria.
- Keep Package Details read-only except for explicit version-scoped actions.
- Do not add new durable storage, public API behavior, manifest schema changes, or package code execution.
