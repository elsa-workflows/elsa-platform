# Tasks: Delete Sync Runs

**Input**: Design documents from `/specs/005-delete-sync-runs/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included because the feature specification and quickstart define independent verification for single deletion, bulk cleanup, active-run protection, API behavior, persistence safety, and admin UI cleanup workflows.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently after the shared foundation is complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on incomplete tasks in the same phase
- **[Story]**: Maps to a user story from [spec.md](./spec.md)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the existing sync history structure and register the shared cleanup service surface before story work begins.

- [X] T001 Add `SyncRunCleanupService` registration beside existing sync services in `src/Elsa.Catalog.Api/Program.cs`
- [X] T002 Create the cleanup service shell and terminal status helper in `src/Elsa.Catalog.Core/Sync/SyncRunCleanupService.cs`
- [X] T003 Add cleanup preview/result records and sync run cleanup store method signatures in `src/Elsa.Catalog.Core/Sync/PackageSyncService.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared contracts, persistence primitives, and fixtures required before any user story can be implemented.

**CRITICAL**: No user story work should begin until this phase is complete.

- [X] T004 [P] Add admin cleanup response contracts for preview and result payloads in `src/Elsa.Catalog.Api/Admin/Sync/AdminSyncContracts.cs`
- [X] T005 [P] Add reusable sync run cleanup test fixture builders for terminal, running, old, recent, and package-linked runs in `tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs`
- [X] T006 [P] Add reusable EF Core cleanup seed helpers for sync runs and linked package catalog records in `tests/Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs`
- [X] T007 Implement EF Core cleanup preview query, single-run delete, bulk delete, and item counting methods in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/SyncRunStore.cs`
- [X] T008 Add structured cleanup logging helpers or logger calls for cleanup scope and counts in `src/Elsa.Catalog.Core/Sync/SyncRunCleanupService.cs`

**Checkpoint**: Foundation ready. User story implementation can now begin in priority order or in parallel by story.

---

## Phase 3: User Story 1 - Delete One Obsolete Sync Run (Priority: P1) MVP

**Goal**: Administrators can delete a completed sync run and its item-level details without changing package catalog data.

**Independent Test**: Create a completed sync run with item-level details and related package catalog records, delete that run, then verify the run and items are gone while packages, versions, manifests, validation results, approvals, and sources remain available.

### Tests for User Story 1

- [X] T009 [P] [US1] Add core tests for single terminal-run deletion, item count reporting, missing-run idempotency, and package-state preservation in `tests/Elsa.Catalog.Core.Tests/SyncRunCleanupServiceTests.cs`
- [X] T010 [P] [US1] Add EF Core persistence tests that deleting one sync run cascades sync items and preserves package versions, validation results, approvals, and sources in `tests/Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs`
- [X] T011 [P] [US1] Add admin API tests for `DELETE /api/admin/sync-runs/{id}` success, no-match response, and authentication in `tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs`
- [X] T012 [P] [US1] Add admin UI component tests for terminal-row delete confirmation, success refresh, and missing-run no-match feedback in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx`

### Implementation for User Story 1

- [X] T013 [US1] Implement single-run cleanup orchestration and idempotent no-match results in `src/Elsa.Catalog.Core/Sync/SyncRunCleanupService.cs`
- [X] T014 [US1] Map `DELETE /api/admin/sync-runs/{id}` to the cleanup service and response contract in `src/Elsa.Catalog.Api/Admin/Sync/AdminSyncEndpoints.cs`
- [X] T015 [US1] Add single-run delete adapter and cache invalidation support in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunApi.ts`
- [X] T016 [US1] Add cleanup result view models and terminal-run action helpers in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunModels.ts`
- [X] T017 [US1] Add per-row delete action, confirmation dialog, pending state, and success/error feedback in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.tsx`

**Checkpoint**: User Story 1 is independently functional and demoable as the MVP cleanup slice.

---

## Phase 4: User Story 2 - Delete Old Sync Runs in Bulk (Priority: P1)

**Goal**: Administrators can preview and delete multiple terminal sync runs completed before an explicit UTC cutoff.

**Independent Test**: Seed recent and old sync runs with multiple statuses, preview and delete runs older than a cutoff, then verify only eligible old terminal history was removed and recent or ineligible runs remain visible.

### Tests for User Story 2

- [X] T018 [P] [US2] Add core tests for bulk preview counts, bulk deletion counts, zero-match cleanup, UTC cutoff comparison, future-cutoff rejection, and recent-run preservation in `tests/Elsa.Catalog.Core.Tests/SyncRunCleanupServiceTests.cs`
- [X] T019 [P] [US2] Add EF Core persistence tests for deleting at least 1,000 eligible historical sync runs while preserving catalog state in `tests/Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs`
- [X] T020 [P] [US2] Add admin API tests for `GET /api/admin/sync-runs/deletion-preview` and `DELETE /api/admin/sync-runs?completedBefore=...` success, malformed cutoff, and future-cutoff validation responses in `tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs`
- [X] T021 [P] [US2] Add admin UI component tests for cutoff entry, preview dialog counts, bulk cleanup confirmation, zero-match state, and list refresh in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx`

### Implementation for User Story 2

- [X] T022 [US2] Implement bulk cleanup preview and bulk delete orchestration with explicit UTC cutoff handling in `src/Elsa.Catalog.Core/Sync/SyncRunCleanupService.cs`
- [X] T023 [US2] Map `GET /api/admin/sync-runs/deletion-preview` and `DELETE /api/admin/sync-runs` endpoints with cutoff validation in `src/Elsa.Catalog.Api/Admin/Sync/AdminSyncEndpoints.cs`
- [X] T024 [US2] Add bulk cleanup preview and delete adapter functions in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunApi.ts`
- [X] T025 [US2] Add cleanup preview normalization, cutoff formatting, and count helpers in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunModels.ts`
- [X] T026 [US2] Add bulk cleanup cutoff control, server preview confirmation, zero-match handling, pending state, and query invalidation in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.tsx`

**Checkpoint**: User Story 2 is independently functional for storage cleanup across accumulated history.

---

## Phase 5: User Story 3 - Protect Active and Important History (Priority: P2)

**Goal**: The system refuses active run deletion and clearly reports eligible, excluded, and deleted counts before and after cleanup.

**Independent Test**: Attempt to delete running and terminal runs through direct and bulk cleanup flows, then verify running runs remain and cleanup responses identify excluded records.

### Tests for User Story 3

- [X] T027 [P] [US3] Add core tests that running sync runs are refused for direct deletion and excluded from bulk cleanup results in `tests/Elsa.Catalog.Core.Tests/SyncRunCleanupServiceTests.cs`
- [X] T028 [P] [US3] Add admin API tests for `409 Conflict` on running-run direct deletion and excluded count reporting for bulk cleanup in `tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs`
- [X] T029 [P] [US3] Add admin UI component tests that running rows hide destructive actions and bulk preview displays excluded-run counts in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx`

### Implementation for User Story 3

- [X] T030 [US3] Enforce terminal-state eligibility and conflict results for non-terminal direct deletion in `src/Elsa.Catalog.Core/Sync/SyncRunCleanupService.cs`
- [X] T031 [US3] Convert non-terminal direct cleanup failures into `409 Conflict` responses in `src/Elsa.Catalog.Api/Admin/Sync/AdminSyncEndpoints.cs`
- [X] T032 [US3] Hide row delete actions for active runs and show excluded-run counts in cleanup confirmations in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.tsx`
- [X] T033 [US3] Update sync run active-state and cleanup eligibility helpers for future status additions in `src/Elsa.Catalog.AdminUi/src/features/sync-runs/syncRunModels.ts`

**Checkpoint**: User Story 3 is independently functional for active-run protection and operator-safe feedback.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation alignment, and cleanup across the feature.

- [X] T034 [P] Update admin API HTTP examples for cleanup preview, single deletion, and bulk deletion in `src/Elsa.Catalog.Api/Elsa.Catalog.Api.http`
- [X] T035 [P] Update quickstart verification notes if endpoint names or frontend commands changed in `specs/005-delete-sync-runs/quickstart.md`
- [X] T036 Review cleanup abstractions against simplicity and deletion-safety constraints in `src/Elsa.Catalog.Core/Sync/SyncRunCleanupService.cs`
- [X] T037 Run core, persistence, API, and admin UI cleanup tests from `specs/005-delete-sync-runs/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - blocks all user stories
- **User Stories (Phase 3+)**: Depend on Foundational phase completion
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational - MVP single-run deletion
- **User Story 2 (P1)**: Can start after Foundational, but can reuse cleanup result models from US1 when implemented sequentially
- **User Story 3 (P2)**: Can start after Foundational, but should be validated after US1 and US2 to harden both deletion paths

### Within Each User Story

- Tests should be written and fail before implementation
- Core cleanup rules before persistence/API wiring where feasible
- Persistence behavior before endpoint behavior
- API contracts before admin UI integration
- Story complete before moving to the next priority checkpoint

### Parallel Opportunities

- Foundational contract, API fixture, and persistence fixture tasks T004-T006 can run in parallel
- US1 tests T009-T012 can run in parallel
- US2 tests T018-T021 can run in parallel
- US3 tests T027-T029 can run in parallel
- UI adapter/model work can run alongside backend endpoint work once response contracts are stable

---

## Parallel Example: User Story 1

```bash
Task: "T009 [P] [US1] Add core tests for single terminal-run deletion, item count reporting, missing-run idempotency, and package-state preservation in tests/Elsa.Catalog.Core.Tests/SyncRunCleanupServiceTests.cs"
Task: "T010 [P] [US1] Add EF Core persistence tests that deleting one sync run cascades sync items and preserves package versions, validation results, approvals, and sources in tests/Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs"
Task: "T011 [P] [US1] Add admin API tests for DELETE /api/admin/sync-runs/{id} success, no-match response, and authentication in tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs"
Task: "T012 [P] [US1] Add admin UI component tests for terminal-row delete confirmation, success refresh, and missing-run no-match feedback in src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx"
```

---

## Parallel Example: User Story 2

```bash
Task: "T018 [P] [US2] Add core tests for bulk preview counts, bulk deletion counts, zero-match cleanup, UTC cutoff comparison, and recent-run preservation in tests/Elsa.Catalog.Core.Tests/SyncRunCleanupServiceTests.cs"
Task: "T019 [P] [US2] Add EF Core persistence tests for deleting at least 1,000 eligible historical sync runs while preserving catalog state in tests/Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs"
Task: "T020 [P] [US2] Add admin API tests for GET /api/admin/sync-runs/deletion-preview and DELETE /api/admin/sync-runs?completedBefore=... success and invalid cutoff responses in tests/Elsa.Catalog.Api.Tests/AdminSyncApiTests.cs"
Task: "T021 [P] [US2] Add admin UI component tests for cutoff entry, preview dialog counts, bulk cleanup confirmation, zero-match state, and list refresh in src/Elsa.Catalog.AdminUi/src/features/sync-runs/SyncRunsPage.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: User Story 1
4. Stop and validate single-run deletion independently
5. Demo completed-run deletion without package catalog side effects

### Incremental Delivery

1. Complete Setup + Foundational
2. Add User Story 1 for single-run cleanup
3. Add User Story 2 for bulk cleanup with preview
4. Add User Story 3 for active-run protection and clearer excluded counts
5. Run quickstart validation and admin UI smoke checks

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Developer A implements core and persistence cleanup behavior
3. Developer B implements admin API contracts and endpoint tests
4. Developer C implements admin UI cleanup controls and component tests
5. Integrate by story checkpoints to keep each slice independently testable

## Notes

- [P] tasks touch different files or test layers and can run in parallel after their phase dependencies are met.
- [Story] labels map tasks to user stories in [spec.md](./spec.md).
- No database migration is planned because existing `SyncRuns` and `SyncRunItems` tables support deletion and already model cascade behavior.
- Do not add automatic scheduled retention in this feature; keep cleanup administrator-initiated.
