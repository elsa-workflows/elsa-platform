# Tasks: Weaver Copilot Agent

**Input**: Design documents from `/specs/034-weaver-copilot-agent/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required for backend configuration/session/tool authorization, API contracts, persistence, and console drawer behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the Weaver subsystem, configuration model, and documentation skeleton.

- [x] T001 Add `src/Elsa.Platform.Weaver.Core/Elsa.Platform.Weaver.Core.csproj` and reference it from `Elsa.Platform.Api`.
- [x] T002 Add `tests/Elsa.Platform.Weaver.Core.Tests/Elsa.Platform.Weaver.Core.Tests.csproj` and include it in the solution/test conventions.
- [x] T003 [P] Create `src/Elsa.Platform.Weaver.Core/Configuration/WeaverOptions.cs` with provider, runtime, telemetry, limits, and feature enablement options.
- [x] T004 [P] Create `docs/weaver-configuration.md` documenting GitHub Copilot-backed mode, BYOK mode, API key environment variables, model settings, limits, telemetry, and disabling Weaver.
- [x] T005 Register Weaver configuration and core services in `src/Elsa.Platform.Api/Program.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, storage, redaction, runtime abstraction, and API contracts required by all stories.

- [x] T006 [P] Create Weaver domain models in `src/Elsa.Platform.Weaver.Core/Sessions/WeaverSessionModels.cs`.
- [x] T007 [P] Create Weaver store abstractions in `src/Elsa.Platform.Weaver.Core/Sessions/IWeaverSessionStore.cs`.
- [x] T008 [P] Create Weaver runtime abstraction in `src/Elsa.Platform.Weaver.Core/Runtime/IWeaverRuntime.cs`.
- [x] T009 [P] Create safe fake runtime in `src/Elsa.Platform.Weaver.Core/Runtime/FakeWeaverRuntime.cs` for development/tests and disabled-provider behavior.
- [x] T010 [P] Create redaction service in `src/Elsa.Platform.Weaver.Core/Safety/WeaverRedactionService.cs`.
- [x] T011 Add EF Core Weaver entities and configuration in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/WeaverEntities.cs`.
- [x] T012 Add `CatalogDbContext` DbSets and model configuration for Weaver entities in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/CatalogDbContext.cs` and `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`.
- [x] T013 Implement EF Core Weaver store in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/WeaverSessionStore.cs`.
- [x] T014 [P] Add unit tests for `WeaverRedactionService` in `tests/Elsa.Platform.Weaver.Core.Tests/WeaverRedactionServiceTests.cs`.
- [x] T015 Add persistence tests for session/message/tool/plan records in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/WeaverSessionStoreTests.cs`.

**Checkpoint**: Weaver has safe configuration, domain models, persistence, redaction, and a runtime abstraction.

---

## Phase 3: User Story 1 - Explain Current Workspace Page (Priority: P1) MVP

**Goal**: Users can open Weaver, create a backend session, send a page-aware prompt, and receive a safe read-only response based on current workspace/page context.

**Independent Test**: Open Weaver on a deployment page, ask for a summary, and verify the response is scoped to the route/workspace and respects disabled/unauthorized states.

### Tests for User Story 1

- [x] T016 [P] [US1] Add API tests for configuration/session/message endpoints in `tests/Elsa.Platform.Api.Tests/WorkspaceWeaverApiTests.cs`.
- [x] T017 [P] [US1] Add console tests for unavailable and connected Weaver drawer states in `src/Elsa.Platform.Console/src/app/AppShell.test.tsx`.

### Implementation for User Story 1

- [x] T018 [US1] Implement `WeaverSessionService` in `src/Elsa.Platform.Weaver.Core/Sessions/WeaverSessionService.cs`.
- [x] T019 [US1] Implement workspace context/read tools in `src/Elsa.Platform.Weaver.Core/Tools/WeaverWorkspaceTools.cs`.
- [x] T020 [US1] Add `src/Elsa.Platform.Api/Workspace/WorkspaceWeaverContracts.cs` and `src/Elsa.Platform.Api/Workspace/WorkspaceWeaverEndpoints.cs`.
- [x] T021 [US1] Map Weaver endpoints from `src/Elsa.Platform.Api/Program.cs`.
- [x] T022 [US1] Add console Weaver models/API hooks in `src/Elsa.Platform.Console/src/features/weaver/weaverModels.ts` and `src/Elsa.Platform.Console/src/features/weaver/weaverApi.ts`.
- [x] T023 [US1] Replace placeholder drawer with backend-backed component in `src/Elsa.Platform.Console/src/features/weaver/WeaverAssistantPanel.tsx` and `src/Elsa.Platform.Console/src/app/AppShell.tsx`.
- [x] T024 [US1] Add query keys for Weaver in `src/Elsa.Platform.Console/src/lib/query/queryClient.tsx`.

**Checkpoint**: User Story 1 works independently with fake provider/runtime and safe read-only behavior.

---

## Phase 4: User Story 2 - Investigate Deployments And Draft Plans (Priority: P1)

**Goal**: Weaver can inspect deployment state and save immutable operational plans.

**Independent Test**: Ask Weaver to prepare a promotion plan and verify a plan card with target, impact, validations, blockers, and rollback path is saved.

### Tests for User Story 2

- [x] T025 [P] [US2] Add core tests for deployment tool summaries in `tests/Elsa.Platform.Weaver.Core.Tests/WeaverDeploymentToolTests.cs`.
- [x] T026 [P] [US2] Add API tests for plan creation/readback in `tests/Elsa.Platform.Api.Tests/WorkspaceWeaverApiTests.cs`.

### Implementation for User Story 2

- [x] T027 [US2] Implement deployment investigation tools in `src/Elsa.Platform.Weaver.Core/Tools/WeaverDeploymentTools.cs`.
- [x] T028 [US2] Implement plan drafting service in `src/Elsa.Platform.Weaver.Core/Plans/WeaverPlanService.cs`.
- [x] T029 [US2] Add plan endpoints to `src/Elsa.Platform.Api/Workspace/WorkspaceWeaverEndpoints.cs`.
- [x] T030 [US2] Render plan cards in `src/Elsa.Platform.Console/src/features/weaver/WeaverPlanCard.tsx`.

**Checkpoint**: User Story 2 works independently without executing any mutation.

---

## Phase 5: User Story 3 - Approve And Execute Agent Plans (Priority: P2)

**Goal**: Approved plans execute through existing platform services with audit-friendly outcomes.

**Independent Test**: Approve a fake or deployment-backed plan and verify permission checks, idempotent execution, and linked resource summaries.

- [x] T031 [P] [US3] Add core tests for approval and idempotent execution in `tests/Elsa.Platform.Weaver.Core.Tests/WeaverPlanExecutionTests.cs`.
- [x] T032 [US3] Implement approval/execution models and store methods in `src/Elsa.Platform.Weaver.Core/Plans` and `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/WeaverSessionStore.cs`.
- [x] T033 [US3] Implement approved fake/deployment execution path in `src/Elsa.Platform.Weaver.Core/Plans/WeaverPlanExecutionService.cs`.
- [x] T034 [US3] Add approval/execute endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceWeaverEndpoints.cs`.
- [x] T035 [US3] Wire approve/reject/execute actions in `src/Elsa.Platform.Console/src/features/weaver/WeaverPlanCard.tsx`.

---

## Phase 6: User Story 4 - Configure Weaver Safely (Priority: P2)

**Goal**: Administrators can configure and diagnose Weaver provider modes, API keys, limits, telemetry, and kill switches.

**Independent Test**: Toggle enabled/provider options and verify configuration endpoint, drawer states, and documentation.

- [x] T036 [P] [US4] Add option validation tests in `tests/Elsa.Platform.Weaver.Core.Tests/WeaverOptionsTests.cs`.
- [x] T037 [US4] Add configuration validation hosted service in `src/Elsa.Platform.Api/Workspace/WeaverConfigurationHostedService.cs`.
- [x] T038 [US4] Complete `docs/weaver-configuration.md` with production, local, and troubleshooting sections.
- [x] T039 [US4] Add unavailable/misconfigured UI state in `src/Elsa.Platform.Console/src/features/weaver/WeaverAssistantPanel.tsx`.

---

## Phase 7: User Story 5 - Audit And Review Agent Activity (Priority: P3)

**Goal**: Admins can inspect safe Weaver sessions, tool calls, plans, approvals, and executions.

**Independent Test**: Run read-only and plan sessions, then verify safe session details are visible without secrets.

- [x] T040 [P] [US5] Add API tests for safe session detail redaction in `tests/Elsa.Platform.Api.Tests/WorkspaceWeaverApiTests.cs`.
- [x] T041 [US5] Add session detail endpoint in `src/Elsa.Platform.Api/Workspace/WorkspaceWeaverEndpoints.cs`.
- [x] T042 [US5] Add session detail UI route/page in `src/Elsa.Platform.Console/src/features/weaver/WeaverSessionPage.tsx` and `src/Elsa.Platform.Console/src/app/routes.tsx`.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [x] T043 Run `dotnet test tests/Elsa.Platform.Weaver.Core.Tests/Elsa.Platform.Weaver.Core.Tests.csproj --no-restore`.
- [x] T044 Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --no-restore`.
- [x] T045 Run `npm run typecheck --prefix src/Elsa.Platform.Console`.
- [x] T046 Run `npm test --prefix src/Elsa.Platform.Console -- Weaver`.
- [x] T047 Run `git diff --check`.
- [x] T048 Self-review all Weaver files for high-priority security, authorization, redaction, and UX issues.
- [ ] T049 Open a PR for Weaver implementation.
- [ ] T050 Merge the PR after checks and self-review pass.

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1) has no dependencies.
- Foundational (Phase 2) depends on setup and blocks all user stories.
- US1 and US2 are both MVP-priority, but US1 should land first because it proves session, runtime, and drawer plumbing.
- US3 depends on US2 plans.
- US4 can progress after foundational work and should complete before production enablement.
- US5 depends on persisted sessions/tool calls/plans.

### Parallel Opportunities

- T003 and T004 can run in parallel.
- T006 through T010 can run in parallel after project setup.
- Tests marked [P] can be written in parallel with different implementation files.
- Console components and backend service tests can be developed in parallel after contracts are stable.

## Implementation Strategy

1. Complete setup and foundational tasks.
2. Deliver US1 with fake provider/runtime and read-only page-aware behavior.
3. Add US2 plan drafting without mutation.
4. Add US3 approval-gated execution.
5. Complete configuration hardening and audit/session review.
6. Run validation, self-review, open PR, and merge.
