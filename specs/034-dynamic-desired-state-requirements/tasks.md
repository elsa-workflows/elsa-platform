# Tasks: Dynamic Desired-State Requirements

**Input**: Design documents from `/specs/034-dynamic-desired-state-requirements/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are required for backend metadata and frontend visibility/submission behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm existing revision and tier capability structures.

- [x] T001 Review current revision creation, tier capability, and validation code in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`, `src/ElsaControl.Deployment.Core/Workspace/DeploymentTierModels.cs`, and `src/ElsaControl.Deployment.Core/Workspace/DeploymentValidationService.cs`
- [x] T002 Update Spec Kit context in `AGENTS.md` to point at `specs/034-dynamic-desired-state-requirements/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared requirement metadata before story-specific UI behavior.

- [x] T003 Add desired-state requirement models in `src/ElsaControl.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`
- [x] T004 Add requirement catalog/service logic in `src/ElsaControl.Deployment.Core/Workspace/DeploymentTierService.cs` or a nearby workspace service file
- [x] T005 Add workspace API response contract in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [x] T006 Add environment desired-state requirements endpoint in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [x] T007 Add console models and API client in `src/ElsaControl.Console/src/features/deployments/deploymentModels.ts` and `src/ElsaControl.Console/src/features/deployments/deploymentApi.ts`
- [x] T008 Add query key support in `src/ElsaControl.Console/src/lib/query/queryClient.tsx`

---

## Phase 3: User Story 1 - Hide Irrelevant Requirements (Priority: P1) 🎯 MVP

**Goal**: Dev/Test revision creation hides production-only observability by default.

**Independent Test**: Open a Dev new revision form and verify observability is hidden while revision creation works.

### Tests for User Story 1

- [x] T009 [P] [US1] Add API test for an environment without observability requirements in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [x] T010 [P] [US1] Add console test that Dev new revision page hides observability in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 1

- [x] T011 [US1] Load requirement metadata in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T012 [US1] Replace the always-visible observability checkbox with a requirements section in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T013 [US1] Submit only artifact records when no requirement or contextual request enables observability in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`

---

## Phase 4: User Story 2 - Show Required Tier Records (Priority: P2)

**Goal**: Observability-required environments show and validate the observability editor.

**Independent Test**: Open a Production new revision form and verify observability is required and submitted.

### Tests for User Story 2

- [x] T014 [P] [US2] Add API test for an observability-required environment in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [x] T015 [P] [US2] Add console test that Production new revision page requires and submits observability in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 2

- [x] T016 [US2] Mark current-tier observability as required in form copy and validation in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T017 [US2] Update revision flow side-panel copy to reflect dynamic requirements in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`

---

## Phase 5: User Story 3 - Support Contextual Validation Fixes (Priority: P3)

**Goal**: Validation deep links can open a supported requirement editor on a source environment that does not require it.

**Independent Test**: Open a Dev new revision form with `includeRequirement=observability-binding` and verify the editor appears with contextual copy.

### Tests for User Story 3

- [x] T018 [P] [US3] Add console test for contextual observability query behavior in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 3

- [x] T019 [US3] Support `includeRequirement=observability-binding` in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`
- [x] T020 [US3] Update validation action links to use the new contextual requirement query in `src/ElsaControl.Console/src/features/deployments/DeploymentsPage.tsx`

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verification, cleanup, and documentation alignment.

- [x] T021 Run focused API tests for deployment workspace behavior
- [x] T022 Run console typecheck and focused deployment page tests
- [x] T023 Run `git diff --check`
- [x] T024 Perform self-review for high-priority correctness, security, permission, and UX issues
- [x] T025 Fix any high-priority issues found during self-review

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup completion and blocks all user stories.
- **User Story 1**: Depends on foundational metadata.
- **User Story 2**: Depends on User Story 1 UI structure.
- **User Story 3**: Depends on User Story 1 UI structure.
- **Polish**: Depends on all selected user stories.

### Parallel Opportunities

- T009 and T010 can run in parallel.
- T014 and T015 can run in parallel.
- T018 can run after the shared UI structure is complete.

## Implementation Strategy

1. Build the backend requirement metadata contract first.
2. Replace hardcoded frontend observability visibility with requirement-driven state.
3. Add required-tier behavior and contextual fix behavior.
4. Verify backend, frontend, and whitespace checks.
