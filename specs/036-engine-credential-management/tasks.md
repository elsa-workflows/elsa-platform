# Tasks: Engine Credential Management UI

**Input**: Design documents from `/specs/036-engine-credential-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Focused Vitest/Testing Library coverage is required because this is primarily a Console UX feature. Backend tests are required only if API behavior changes.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm baseline and prepare shared code boundaries.

- [X] T001 Inspect current deployment routing/navigation and credential panel usage in `src/Elsa.Platform.Console/src/app/routes.tsx`, `src/Elsa.Platform.Console/src/app/AppShell.tsx`, and `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T002 Inspect existing deployment credential API/query keys and test helpers in `src/Elsa.Platform.Console/src/features/deployments/deploymentApi.ts`, `src/Elsa.Platform.Console/src/lib/query/queryClient.tsx`, and `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create reusable credential-management building blocks shared by setup wizard and standalone page.

- [X] T003 Extract or adapt the existing `SecretStoresPanel` into a reusable engine credential management component in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T004 Add reusable mutation/query invalidation helpers for store/reference create, update, rotate, usage, and archive flows in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T005 Ensure credential management copy consistently says engine credentials and platform-to-engine credentials in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: Shared component can still serve the new application setup credential step.

---

## Phase 3: User Story 1 - Find and Manage Engine Credentials (Priority: P1) MVP

**Goal**: Administrators can discover a dedicated workspace-level engine credential management surface outside the setup wizard.

**Independent Test**: Open the Console, navigate to Deployments -> Engine credentials, and verify existing stores/references render with safe metadata and engine-only scope.

### Tests for User Story 1

- [X] T006 [P] [US1] Add route/navigation test coverage for the Engine credentials page in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [X] T007 [P] [US1] Add read-only/empty state test coverage for the standalone credential management page in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [X] T008 [US1] Add `DeploymentCredentialsPage` export and page shell in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T009 [US1] Add `/admin/deployments/credentials` route in `src/Elsa.Platform.Console/src/app/routes.tsx`
- [X] T010 [US1] Add an `Engine credentials` navigation item under Deployments in `src/Elsa.Platform.Console/src/app/AppShell.tsx`
- [X] T011 [US1] Render active stores/references by default and archived records through an explicit filter in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 1 is fully functional and testable independently.

---

## Phase 4: User Story 2 - Create and Update Stores and References (Priority: P1)

**Goal**: Administrators can create and edit engine credential stores and references from the dedicated surface.

**Independent Test**: Create a store/reference for each supported store type from the standalone page and verify raw local credential values are write-only.

### Tests for User Story 2

- [X] T012 [P] [US2] Add standalone create-store and create-reference tests in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [X] T013 [P] [US2] Add edit metadata and local rotation tests in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 2

- [X] T014 [US2] Add standalone store create/edit form behavior in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T015 [US2] Add standalone credential reference create/edit form behavior in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T016 [US2] Add local encrypted credential rotation behavior with non-empty value validation in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T017 [US2] Invalidate store/reference/cockpit queries after mutations in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 2 works without entering the new application setup wizard.

---

## Phase 5: User Story 3 - Understand Usage Before Lifecycle Actions (Priority: P2)

**Goal**: Administrators can inspect engine usage before archive or rotation actions.

**Independent Test**: Assign a reference to engines, open the management page, expand usage, and confirm lifecycle confirmations show affected engines.

### Tests for User Story 3

- [X] T018 [P] [US3] Add usage disclosure and archive confirmation tests in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [X] T019 [P] [US3] Add rotation usage-disclosure test for local encrypted references in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 3

- [X] T020 [US3] Add on-demand usage expansion with application/environment/engine context in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T021 [US3] Add archive confirmation that discloses usage before submitting store/reference archive in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T022 [US3] Add rotation confirmation or inline usage disclosure before local credential rotation in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 3 provides impact context before disruptive actions.

---

## Phase 6: User Story 4 - Reuse Credentials in Engine Setup (Priority: P2)

**Goal**: Administrators can move from engine setup to credential management and newly created references are eligible for assignment in the same workspace.

**Independent Test**: Start engine registration with no references, follow the manage credentials link, create a reference, return to registration, and select it.

### Tests for User Story 4

- [X] T023 [P] [US4] Add engine registration empty-credential link test in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`
- [X] T024 [P] [US4] Add query-refresh test proving created active references are selectable in engine setup in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 4

- [X] T025 [US4] Add manage-credentials route links from engine registration/edit empty credential states in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T026 [US4] Ensure credential creation invalidates engine setup options for the selected workspace in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 4 connects standalone management with engine setup.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verification, cleanup, and documentation alignment.

- [X] T027 [P] Update quickstart or contract notes if implementation changes route labels or lifecycle behavior in `specs/036-engine-credential-management/quickstart.md` and `specs/036-engine-credential-management/contracts/console-engine-credential-management-ux.md`
- [X] T028 Run `npm run test -- src/features/deployments/DeploymentsPage.test.tsx`
- [X] T029 Run `npm run typecheck`
- [X] T030 Run `git diff --check`
- [X] T031 Run targeted .NET API tests only if API behavior changed: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --no-restore --filter WorkspaceDeploymentApiTests` (not required; no API behavior changed)
- [X] T032 Perform self-review for critical issues across credential safety, permission gating, workspace scoping, and query invalidation
- [ ] T033 Open PR, address critical review findings, and merge after checks pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup completion and blocks user stories
- **User Story 1 (Phase 3)**: Depends on Foundational; MVP discovery surface
- **User Story 2 (Phase 4)**: Depends on Foundational; can proceed after or alongside US1 once route shell exists
- **User Story 3 (Phase 5)**: Depends on US2 lifecycle controls
- **User Story 4 (Phase 6)**: Depends on US1 route and US2 query invalidation
- **Polish (Phase 7)**: Depends on implemented stories

### User Story Dependencies

- **US1**: Independent MVP once shared credential component exists
- **US2**: Independent management actions, but delivered through US1 page
- **US3**: Requires references and lifecycle actions from US2
- **US4**: Requires standalone route from US1 and created-reference invalidation from US2

### Parallel Opportunities

- T006 and T007 can run in parallel.
- T012 and T013 can run in parallel after US1 tests establish page harness.
- T018 and T019 can run in parallel after lifecycle controls exist.
- T023 and T024 can run in parallel after route links and query invalidation behavior exist.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 to make the standalone page discoverable and read-only-safe.
3. Validate US1 independently with route/navigation/list tests.

### Incremental Delivery

1. Add create/edit/rotate behavior through US2.
2. Add usage and lifecycle confirmation through US3.
3. Connect engine setup links and refresh behavior through US4.
4. Run full verification, self-review, PR, and merge.
