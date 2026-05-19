# Tasks: Elsa Package Catalog Admin Dashboard UI

**Input**: Design documents from `/specs/003-admin-dashboard-ui/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included because the feature specification defines a testing strategy for UI workflows, API contract behavior, accessibility, responsive behavior, and error states.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently after the shared foundation is complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on incomplete tasks in the same phase
- **[Story]**: Maps to a user story from [spec.md](./spec.md)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the admin UI project and test harness without implementing story behavior.

- [X] T001 Create the Vite React TypeScript admin UI project manifest in `src/Elsa.Catalog.AdminUi/package.json`
- [X] T002 Configure TypeScript, Vite, and path aliases in `src/Elsa.Catalog.AdminUi/tsconfig.json` and `src/Elsa.Catalog.AdminUi/vite.config.ts`
- [X] T003 Configure TailwindCSS, dark-mode theme tokens, and global stylesheet entry in `src/Elsa.Catalog.AdminUi/tailwind.config.ts` and `src/Elsa.Catalog.AdminUi/src/styles.css`
- [X] T004 [P] Configure frontend unit/component test runner in `src/Elsa.Catalog.AdminUi/vitest.config.ts` and `src/Elsa.Catalog.AdminUi/src/test/setupTests.ts`
- [X] T005 [P] Configure browser E2E test project in `tests/Elsa.Catalog.AdminUi.E2E/package.json` and `tests/Elsa.Catalog.AdminUi.E2E/playwright.config.ts`
- [X] T006 Add admin UI development configuration sample in `src/Elsa.Catalog.AdminUi/.env.example`
- [X] T007 Add admin UI build and test notes to `src/Elsa.Catalog.AdminUi/README.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared routing, API, query, layout, status, and test utilities required before any user story work begins.

**CRITICAL**: No user story work should begin until this phase is complete.

- [X] T008 Create the React application entry point in `src/Elsa.Catalog.AdminUi/src/main.tsx`
- [X] T009 Create the root route configuration with only Overview, Sources, Packages, and Sync Runs destinations in `src/Elsa.Catalog.AdminUi/src/app/routes.tsx`
- [X] T010 Create the restrained admin shell and primary navigation in `src/Elsa.Catalog.AdminUi/src/app/AppShell.tsx`
- [X] T011 [P] Create shared button, input, dialog, badge, table, tabs, and empty-state UI primitive exports in `src/Elsa.Catalog.AdminUi/src/components/ui/index.tsx`
- [X] T012 [P] Create shared status badge mapping utilities in `src/Elsa.Catalog.AdminUi/src/lib/status/statusBadges.ts`
- [X] T013 Create the authenticated HTTP client with admin API key support and typed error normalization in `src/Elsa.Catalog.AdminUi/src/lib/api/httpClient.ts`
- [X] T014 Create TanStack Query provider, query key helpers, and polling defaults in `src/Elsa.Catalog.AdminUi/src/lib/query/queryClient.ts`
- [X] T015 [P] Create shared date, duration, JSON formatting, and text truncation helpers in `src/Elsa.Catalog.AdminUi/src/lib/formatters.ts`
- [X] T016 [P] Create shared route-aware loading, stale-refresh, unauthorized, not-found, and unexpected-error views in `src/Elsa.Catalog.AdminUi/src/components/states/RequestStateViews.tsx`
- [X] T017 [P] Create MSW-style admin API mock handlers for sources, packages, validation, manifests, and sync runs in `src/Elsa.Catalog.AdminUi/src/test/adminApiHandlers.ts`
- [X] T018 [P] Create representative dashboard fixture data in `src/Elsa.Catalog.AdminUi/src/test/fixtures.ts`
- [X] T019 Add E2E test helpers for API stubbing, navigation, and admin credentials in `tests/Elsa.Catalog.AdminUi.E2E/helpers/adminUiTestHelpers.ts`
- [X] T020 Verify the root app renders the four MVP navigation entries with a component test in `src/Elsa.Catalog.AdminUi/src/app/AppShell.test.tsx`
- [X] T020a Expose and render authenticated application build metadata in `src/Elsa.Catalog.Api/Admin/Application/AdminApplicationEndpoints.cs`, `src/Elsa.Catalog.AdminUi/src/app/applicationApi.ts`, and `src/Elsa.Catalog.AdminUi/src/app/AppShell.tsx`

**Checkpoint**: Foundation ready. User story implementation can now begin in priority order or in parallel by story.

---

## Phase 3: User Story 1 - Operate Package Sources (Priority: P1) MVP

**Goal**: Administrators can list, create, edit, enable, disable, soft-delete, sync, and pattern-test package sources.

**Independent Test**: Start with no sources, create a source with include/exclude patterns, verify it appears with health information, test sample package IDs, disable and re-enable it, trigger sync, and soft-delete it after confirmation.

### Tests for User Story 1

- [X] T021 [P] [US1] Add API tests for source soft-delete, active source filtering, source status, last successful sync, and package count in `tests/Elsa.Catalog.Api.Tests/AdminSourcesApiTests.cs`
- [X] T022 [P] [US1] Add source pattern tester parity tests for include/exclude precedence in `src/Elsa.Catalog.AdminUi/src/features/sources/patternTester.test.ts`
- [X] T023 [P] [US1] Add component tests for source list empty, loading, populated, stale, and filtered states in `src/Elsa.Catalog.AdminUi/src/features/sources/SourcesPage.test.tsx`
- [X] T024 [P] [US1] Add component tests for create/edit validation and unsaved value preservation in `src/Elsa.Catalog.AdminUi/src/features/sources/SourceForm.test.tsx`
- [X] T025 [P] [US1] Add E2E test for create, pattern-test, sync, disable, enable, and soft-delete source workflow in `tests/Elsa.Catalog.AdminUi.E2E/sources.spec.ts`

### Implementation for User Story 1

- [X] T026 [US1] Add soft-delete, health, and polling interval fields to package source model in `src/Elsa.Catalog.Core/Packages/PackageModels.cs`
- [X] T027 [US1] Update EF Core source mapping for soft-delete, health, and polling interval fields in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T028 [US1] Add provider migrations for source soft-delete, health, and polling interval fields in `src/Elsa.Catalog.Persistence.SqliteMigrations/Migrations/20260515_AddPackageSourceSoftDeleteAndHealth.cs` and `src/Elsa.Catalog.Persistence.SqlServerMigrations/Migrations/20260515_AddPackageSourceSoftDeleteAndHealth.cs`
- [X] T029 [US1] Update source store queries to hide soft-deleted sources by default and preserve history in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/PackageSourceStore.cs`
- [X] T030 [US1] Update source service soft-delete behavior, polling interval validation, and source health projection in `src/Elsa.Catalog.Core/Sources/PackageSourceService.cs`
- [X] T031 [US1] Update admin source contracts with status, lastSuccessfulSyncAt, packageCount, softDeletedAt, and pollingInterval fields in `src/Elsa.Catalog.Api/Admin/Sources/AdminSourceContracts.cs`
- [X] T032 [US1] Update admin source endpoints to expose active sources, soft-delete semantics, source health fields, and sync-linkable IDs in `src/Elsa.Catalog.Api/Admin/Sources/AdminSourceEndpoints.cs`
- [X] T033 [US1] Create source API adapter functions in `src/Elsa.Catalog.AdminUi/src/features/sources/sourceApi.ts`
- [X] T034 [US1] Create source view models, polling interval mapping, and status derivation helpers in `src/Elsa.Catalog.AdminUi/src/features/sources/sourceModels.ts`
- [X] T035 [US1] Implement include/exclude pattern tester logic with case-insensitive glob matching in `src/Elsa.Catalog.AdminUi/src/features/sources/patternTester.ts`
- [X] T036 [US1] Implement Sources list page with table columns, status badges, row actions, empty states, and refresh handling in `src/Elsa.Catalog.AdminUi/src/features/sources/SourcesPage.tsx`
- [X] T037 [US1] Implement create/edit source form with field validation, pattern tester preview, and failed-save value preservation in `src/Elsa.Catalog.AdminUi/src/features/sources/SourceForm.tsx`
- [X] T038 [US1] Implement source detail view with guaranteed health fields, recent sync evidence, and source sync action in `src/Elsa.Catalog.AdminUi/src/features/sources/SourceDetailsPage.tsx`
- [X] T039 [US1] Implement source disable/enable and soft-delete confirmation dialogs in `src/Elsa.Catalog.AdminUi/src/features/sources/SourceActions.tsx`
- [X] T040 [US1] Wire Sources routes into the app route tree in `src/Elsa.Catalog.AdminUi/src/app/routes.tsx`

**Checkpoint**: User Story 1 is independently functional and demoable as the MVP source-management slice.

---

## Phase 4: User Story 2 - Review and Approve Packages (Priority: P1)

**Goal**: Administrators can browse packages, filter operational states, and approve or reject selected package versions with required rejection reasons.

**Independent Test**: Seed approved, pending, rejected, invalid, suspicious, and unlisted package versions; verify search, filters, table state, details navigation, single approval, single rejection with required reason, and bulk approval/rejection.

### Tests for User Story 2

- [ ] T041 [P] [US2] Add API tests that every package version rejection requires a non-empty reason in `tests/Elsa.Catalog.Api.Tests/AdminApprovalApiTests.cs`
- [ ] T042 [P] [US2] Add API tests that package identity approval endpoints are not needed by dashboard version workflows in `tests/Elsa.Catalog.Api.Tests/AdminApprovalApiTests.cs`
- [ ] T043 [P] [US2] Add component tests for package search, filters, sorting, selection, and bulk selected count in `src/Elsa.Catalog.AdminUi/src/features/packages/PackagesPage.test.tsx`
- [ ] T044 [P] [US2] Add component tests for approve/reject dialogs and required rejection reason validation in `src/Elsa.Catalog.AdminUi/src/features/packages/PackageVersionActions.test.tsx`
- [ ] T045 [P] [US2] Add E2E test for pending package-version approval and rejection workflow in `tests/Elsa.Catalog.AdminUi.E2E/packages-approval.spec.ts`

### Implementation for User Story 2

- [ ] T046 [US2] Update approval request handling to reject empty package version rejection reasons in `src/Elsa.Catalog.Api/Admin/Packages/AdminApprovalEndpoints.cs`
- [ ] T047 [US2] Update approval store/service to persist and expose version rejection reasons in `src/Elsa.Catalog.Core/Approvals/ApprovalService.cs`
- [ ] T048 [US2] Update approval persistence for rejection reasons in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/ApprovalStore.cs`
- [ ] T049 [US2] Add package list query/filter/sort support needed by the dashboard in `src/Elsa.Catalog.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [ ] T050 [US2] Update admin package contracts with sourceId, updatedAt, feature count, aggregate statuses, and version rejection reason fields in `src/Elsa.Catalog.Api/Admin/Packages/AdminPackageContracts.cs`
- [ ] T051 [US2] Create package API adapter functions for list, details, approve version, reject version, optional re-sync, optional revalidate, optional recompute metadata, and repeated bulk mutations in `src/Elsa.Catalog.AdminUi/src/features/packages/packageApi.ts`
- [ ] T052 [US2] Create package list and package-version view models in `src/Elsa.Catalog.AdminUi/src/features/packages/packageModels.ts`
- [ ] T053 [US2] Implement Packages page with URL-backed search, filters, sorting, selection, bulk action bar, and status badges in `src/Elsa.Catalog.AdminUi/src/features/packages/PackagesPage.tsx`
- [ ] T054 [US2] Implement package-version action dialogs for approve, reject, optional re-sync, optional revalidate, optional recompute metadata, required rejection reason, unavailable-action messaging, and partial-failure reporting in `src/Elsa.Catalog.AdminUi/src/features/packages/PackageVersionActions.tsx`
- [ ] T055 [US2] Implement package detail shell and version selector without package identity approval controls in `src/Elsa.Catalog.AdminUi/src/features/packages/PackageDetailsPage.tsx`
- [ ] T056 [US2] Wire Packages routes into the app route tree in `src/Elsa.Catalog.AdminUi/src/app/routes.tsx`

**Checkpoint**: User Story 2 is independently functional with package-version approval and rejection workflows.

---

## Phase 5: User Story 3 - Diagnose Package Manifests and Validation (Priority: P1)

**Goal**: Administrators can inspect package version metadata, validation findings, suspicious changes, visibility explanations, and raw/formatted manifest JSON.

**Independent Test**: Load a package with features, settings, compatibility metadata, validation warnings/errors, suspicious hashes, and a raw manifest; verify each diagnostic section is readable and no manifest editing is possible.

### Tests for User Story 3

- [ ] T057 [P] [US3] Add API tests for admin package version manifest retrieval and manifest hash fields in `tests/Elsa.Catalog.Api.Tests/AdminPackagesApiTests.cs`
- [ ] T058 [P] [US3] Add component tests for validation issue grouping, unknown codes, long messages, and missing paths in `src/Elsa.Catalog.AdminUi/src/features/packages/ValidationPanel.test.tsx`
- [ ] T059 [P] [US3] Add component tests for formatted, raw-only, and unavailable manifest viewer states in `src/Elsa.Catalog.AdminUi/src/features/packages/ManifestViewer.test.tsx`
- [ ] T060 [P] [US3] Add component tests for public visibility explanation combinations in `src/Elsa.Catalog.AdminUi/src/features/packages/VisibilityExplanation.test.tsx`
- [ ] T061 [P] [US3] Add E2E test for invalid package version diagnostics and manifest inspection in `tests/Elsa.Catalog.AdminUi.E2E/package-diagnostics.spec.ts`

### Implementation for User Story 3

- [ ] T062 [US3] Add or extend admin package version detail endpoint with manifest hash, suspicious hash, feature metadata, visibility inputs, supported action flags, and raw manifest JSON in `src/Elsa.Catalog.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [ ] T063 [US3] Normalize admin validation result contract to expose finding arrays for UI consumption in `src/Elsa.Catalog.Api/Admin/Packages/AdminValidationEndpoints.cs`
- [ ] T064 [US3] Create validation finding normalization helpers for current and future response shapes in `src/Elsa.Catalog.AdminUi/src/features/packages/validationModels.ts`
- [ ] T065 [US3] Implement feature metadata section for package version details in `src/Elsa.Catalog.AdminUi/src/features/packages/FeaturesPanel.tsx`
- [ ] T066 [US3] Implement validation diagnostics panel with grouped severity, code, message, and path rendering in `src/Elsa.Catalog.AdminUi/src/features/packages/ValidationPanel.tsx`
- [ ] T067 [US3] Implement read-only manifest viewer with formatted JSON, raw fallback, and collapsible sections in `src/Elsa.Catalog.AdminUi/src/features/packages/ManifestViewer.tsx`
- [ ] T068 [US3] Implement visibility explanation component for approval, validation, listing, and suspicious states in `src/Elsa.Catalog.AdminUi/src/features/packages/VisibilityExplanation.tsx`
- [ ] T069 [US3] Integrate features, validation, manifest, visibility explanation, supported-action flags, and version actions into package version detail route in `src/Elsa.Catalog.AdminUi/src/features/packages/PackageVersionDetailsPage.tsx`

**Checkpoint**: User Story 3 is independently functional for package diagnostics and manifest inspection.

---

## Phase 6: User Story 4 - Inspect Synchronization Runs (Priority: P2)

**Goal**: Administrators can browse sync runs, inspect run details, understand failures, and follow source/package links for troubleshooting.

**Independent Test**: Seed scheduled, manual all-source, manual source, and manual package sync runs with completed, failed, running, canceled, and completed-with-errors states; verify summary rows, filtering, polling, cancellation, details, and diagnostic links.

### Tests for User Story 4

- [ ] T070 [P] [US4] Add API tests for sync run summary counters, duration inputs, item diagnostics, and run cancellation in `tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs`
- [ ] T071 [P] [US4] Add component tests for sync run list states, status filters, polling refresh, active run labels, and cancel actions in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx`
- [ ] T072 [P] [US4] Add component tests for sync run details timeline, grouped failures, warnings, and diagnostic links in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunDetailsPage.test.tsx`
- [ ] T073 [P] [US4] Add E2E test for failed sync run troubleshooting workflow in `tests/Elsa.Catalog.AdminUi.E2E/sync-runs.spec.ts`

### Implementation for User Story 4

- [ ] T074 [US4] Update sync contracts to expose summary counters in typed form while preserving existing JSON counters in `src/Elsa.Catalog.Api/Admin/Sync/AdminSyncContracts.cs`
- [ ] T075 [US4] Update sync run endpoint mapping for duration-ready fields, item diagnostics, and cancel requests in `src/Elsa.Catalog.Api/Admin/Sync/AdminSyncEndpoints.cs`
- [ ] T076 [US4] Create sync run API adapter functions, including cancel, in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunApi.ts`
- [ ] T077 [US4] Create sync run view models and summary counter helpers, including canceled status handling, in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunModels.ts`
- [ ] T078 [US4] Implement Sync Runs page with table columns, filters, active-run polling, cancel controls, empty states, and manual refresh in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.tsx`
- [ ] T079 [US4] Implement Sync Run Details page with summary, timeline, discovered/downloaded/validated item groups, failures, warnings, and links in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunDetailsPage.tsx`
- [ ] T080 [US4] Wire Sync Runs routes into the app route tree in `src/Elsa.Catalog.AdminUi/src/app/routes.tsx`

**Checkpoint**: User Story 4 is independently functional for sync troubleshooting.

---

## Phase 7: User Story 5 - Understand System State at a Glance (Priority: P2)

**Goal**: Administrators can open a lightweight overview that summarizes operational status and links to filtered Sources, Packages, and Sync Runs screens.

**Independent Test**: Seed healthy sources, failed syncs, pending approvals, invalid packages, and recent sync activity; verify the overview shows concise status and links to filtered screens without charts or analytics-heavy presentation.

### Tests for User Story 5

- [ ] T081 [P] [US5] Add component tests for overview status cards, recent sync activity, healthy empty state, and filtered links in `src/Elsa.Catalog.AdminUi/src/features/overview/OverviewPage.test.tsx`
- [ ] T082 [P] [US5] Add E2E test for overview-to-filtered-screen navigation in `tests/Elsa.Catalog.AdminUi.E2E/overview.spec.ts`

### Implementation for User Story 5

- [ ] T083 [US5] Create overview data aggregation adapter from sources, packages, and sync runs APIs in `src/Elsa.Catalog.AdminUi/src/features/overview/overviewApi.ts`
- [ ] T084 [US5] Create overview view models for healthy sources, failed syncs, pending approvals, invalid packages, last successful sync, and recent activity in `src/Elsa.Catalog.AdminUi/src/features/overview/overviewModels.ts`
- [ ] T085 [US5] Implement Overview page with compact operational summaries and no analytics charts in `src/Elsa.Catalog.AdminUi/src/features/overview/OverviewPage.tsx`
- [ ] T086 [US5] Wire Overview route as `/admin` redirect target and `/admin/overview` page in `src/Elsa.Catalog.AdminUi/src/app/routes.tsx`

**Checkpoint**: User Story 5 is independently functional as the dashboard landing page.

---

## Phase 8: User Story 6 - Handle Admin API Errors Predictably (Priority: P2)

**Goal**: Administrators receive clear, recoverable feedback for unauthorized, validation, conflict, not-found, network, stale-refresh, long-running, and partial bulk failures.

**Independent Test**: Simulate unauthorized responses, validation errors, conflicts, network failures, long-running operations, empty responses, and partial bulk failures; verify visible states and recovery actions across the dashboard.

### Tests for User Story 6

- [ ] T087 [P] [US6] Add HTTP client tests for unauthorized, forbidden, validation, conflict, not-found, unavailable, and unexpected error normalization in `src/Elsa.Catalog.AdminUi/src/lib/api/httpClient.test.ts`
- [ ] T088 [P] [US6] Add component tests for stale refresh and protected-data access states in `src/Elsa.Catalog.AdminUi/src/components/states/RequestStateViews.test.tsx`
- [ ] T089 [P] [US6] Add E2E test for unauthorized access, source validation error preservation, and partial bulk failure display in `tests/Elsa.Catalog.AdminUi.E2E/error-states.spec.ts`

### Implementation for User Story 6

- [ ] T090 [US6] Extend HTTP client result types with stale-data, field-error, conflict, and partial-failure metadata in `src/Elsa.Catalog.AdminUi/src/lib/api/httpClient.ts`
- [ ] T091 [US6] Implement reusable mutation confirmation and pending-state helpers in `src/Elsa.Catalog.AdminUi/src/lib/query/mutations.ts`
- [ ] T092 [US6] Update shared request state views for unauthorized, stale, validation, conflict, not-found, and retry states in `src/Elsa.Catalog.AdminUi/src/components/states/RequestStateViews.tsx`
- [ ] T093 [US6] Integrate partial-failure summaries into source and package bulk/action flows in `src/Elsa.Catalog.AdminUi/src/components/states/PartialFailureSummary.tsx`
- [ ] T094 [US6] Add route-level error boundaries around admin routes in `src/Elsa.Catalog.AdminUi/src/app/AdminErrorBoundary.tsx`

**Checkpoint**: User Story 6 is independently functional for recoverable admin API failure handling.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Verify quality gates, accessibility, responsiveness, docs, and final quickstart behavior across all completed stories.

- [ ] T095 [P] Run axe-focused accessibility and dark-mode status contrast checks for navigation, dialogs, tables, status badges, and manifest viewer in `tests/Elsa.Catalog.AdminUi.E2E/accessibility.spec.ts`
- [ ] T096 [P] Add responsive layout and 2-second initial-content performance tests for representative source, package, and sync-run datasets in `tests/Elsa.Catalog.AdminUi.E2E/responsive.spec.ts`
- [ ] T097 [P] Add frontend build, test, lint, and typecheck commands to CI documentation in `src/Elsa.Catalog.AdminUi/README.md`
- [ ] T098 Verify the quickstart end-to-end and record any implementation-specific notes in `specs/003-admin-dashboard-ui/quickstart.md`
- [ ] T099 Run `dotnet test` from repository root and fix regressions documented in `specs/003-admin-dashboard-ui/quickstart.md`
- [ ] T100 Run `npm test`, `npm run build`, and `npm run e2e` from `src/Elsa.Catalog.AdminUi/package.json` and fix regressions documented in `src/Elsa.Catalog.AdminUi/README.md`
- [ ] T101 Review frontend dependencies and abstractions against simplicity rules in `src/Elsa.Catalog.AdminUi/package.json`
- [ ] T102 Confirm no Settings route, package identity approval controls, hard-delete controls, realtime streaming UI, or manifest editing affordances exist in `src/Elsa.Catalog.AdminUi/src/app/routes.tsx`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **US1 Sources (Phase 3)**: Depends on Foundational; recommended MVP first.
- **US2 Package Approval (Phase 4)**: Depends on Foundational; can run in parallel with US1 after shared app/API client exists, but benefits from status components from Foundation.
- **US3 Package Diagnostics (Phase 5)**: Depends on Foundational; can run in parallel with US2 but shares package route files, so coordinate changes in `src/Elsa.Catalog.AdminUi/src/features/packages/`.
- **US4 Sync Runs (Phase 6)**: Depends on Foundational and can run independently of package approval.
- **US5 Overview (Phase 7)**: Depends on source, package, and sync adapters from US1, US2, and US4.
- **US6 Error Handling (Phase 8)**: Depends on Foundational and should be integrated before final validation of all stories.
- **Polish (Phase 9)**: Depends on all desired stories.

### User Story Dependencies

- **US1 (P1)**: Independent MVP after Foundation.
- **US2 (P1)**: Independent after Foundation, with package route coordination.
- **US3 (P1)**: Independent after Foundation, but shares package detail area with US2.
- **US4 (P2)**: Independent after Foundation.
- **US5 (P2)**: Depends on adapters/data from US1, US2, and US4.
- **US6 (P2)**: Cross-cutting; can begin after Foundation but should be completed before final story acceptance.

### Within Each User Story

- Tests are written before implementation tasks in each story phase.
- Backend API contract changes precede frontend adapter usage.
- Frontend adapters and view models precede pages/components that consume them.
- Route wiring comes after page implementation.
- Each checkpoint should be validated before moving to the next priority story.

## Parallel Opportunities

- Setup tasks T004 and T005 can run in parallel with T006 and T007 after T001-T003 are clear.
- Foundational UI primitives, status utilities, formatters, mock handlers, and fixtures can run in parallel: T011, T012, T015, T017, T018.
- US1 API tests, pattern tester tests, component tests, and E2E test scaffolding can run in parallel: T021-T025.
- US2 tests can run in parallel with backend approval contract updates: T041-T045 alongside T046-T050.
- US3 validation, manifest, and visibility tests can run in parallel because they cover different files: T058-T060.
- US4 list and detail tests can run in parallel: T071-T072.
- US5 overview component and E2E tests can run in parallel: T081-T082.
- US6 HTTP client, request state, and E2E error tests can run in parallel: T087-T089.
- Polish accessibility and responsive E2E tests can run in parallel: T095-T096.

## Parallel Example: User Story 1

```text
Task: "T021 [P] [US1] Add API tests for source soft-delete, active source filtering, source status, last successful sync, and package count in tests/Elsa.Catalog.Api.Tests/AdminSourcesApiTests.cs"
Task: "T022 [P] [US1] Add source pattern tester parity tests for include/exclude precedence in src/Elsa.Catalog.AdminUi/src/features/sources/patternTester.test.ts"
Task: "T023 [P] [US1] Add component tests for source list empty, loading, populated, stale, and filtered states in src/Elsa.Catalog.AdminUi/src/features/sources/SourcesPage.test.tsx"
Task: "T024 [P] [US1] Add component tests for create/edit validation and unsaved value preservation in src/Elsa.Catalog.AdminUi/src/features/sources/SourceForm.test.tsx"
```

## Parallel Example: User Story 2

```text
Task: "T043 [P] [US2] Add component tests for package search, filters, sorting, selection, and bulk selected count in src/Elsa.Catalog.AdminUi/src/features/packages/PackagesPage.test.tsx"
Task: "T044 [P] [US2] Add component tests for approve/reject dialogs and required rejection reason validation in src/Elsa.Catalog.AdminUi/src/features/packages/PackageVersionActions.test.tsx"
Task: "T045 [P] [US2] Add E2E test for pending package-version approval and rejection workflow in tests/Elsa.Catalog.AdminUi.E2E/packages-approval.spec.ts"
```

## Parallel Example: User Story 4

```text
Task: "T071 [P] [US4] Add component tests for sync run list states, status filters, polling refresh, and active run labels in src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx"
Task: "T072 [P] [US4] Add component tests for sync run details timeline, grouped failures, warnings, and diagnostic links in src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunDetailsPage.test.tsx"
Task: "T073 [P] [US4] Add E2E test for failed sync run troubleshooting workflow in tests/Elsa.Catalog.AdminUi.E2E/sync-runs.spec.ts"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundation.
3. Complete Phase 3 source management.
4. Validate source create/edit/pattern-test/sync/disable/enable/soft-delete independently.
5. Stop and demo the operational source-management slice before package workflows.

### Incremental Delivery

1. Deliver US1 Sources as the MVP.
2. Add US2 package-version approval and rejection.
3. Add US3 package diagnostics and manifest inspection.
4. Add US4 sync-run troubleshooting.
5. Add US5 overview after source/package/sync data adapters exist.
6. Complete US6 error handling across all workflows before release.

### Parallel Team Strategy

1. Team completes setup and foundational app/API utilities together.
2. One developer handles source backend/API changes while another handles source UI tests.
3. Package approval and package diagnostics can proceed together with careful coordination in `src/Elsa.Catalog.AdminUi/src/features/packages/`.
4. Sync Runs can proceed independently in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/`.
5. Overview is integrated after the source, package, and sync adapters stabilize.

## Notes

- [P] tasks touch different files and can run in parallel after their phase prerequisites are met.
- [US1] through [US6] labels map to user stories in [spec.md](./spec.md).
- All rejection paths must require a reason.
- Package identity approval endpoints must not be surfaced in the MVP UI.
- Source removal must be soft-delete only.
- No Settings route, realtime streaming UI, hard-delete control, or manifest editor should appear in the MVP.
