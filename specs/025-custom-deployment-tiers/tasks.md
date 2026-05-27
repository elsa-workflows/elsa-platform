# Tasks: Custom Deployment Tiers

**Input**: Design documents from `specs/025-custom-deployment-tiers/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Test tasks are included because the specification and plan define independent test criteria and exit gates for each story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and does not depend on incomplete tasks.
- **[Story]**: Maps task to a specific user story: [US1], [US2], [US3], or [US4].
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare shared files and establish the implementation surface for custom deployment tiers.

- [X] T001 Create deployment tier domain file in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierModels.cs`
- [X] T002 Create deployment tier service file in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierService.cs`
- [X] T003 Create deployment tier store contract in `src/Elsa.Platform.Deployment.Core/Workspace/IWorkspaceDeploymentTierStore.cs`
- [ ] T004 [P] Create core tier test file in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentTierServiceTests.cs`
- [ ] T005 [P] Create persistence tier test file in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceTierPersistenceTests.cs`
- [ ] T006 [P] Create API tier test file in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTierApiTests.cs`
- [ ] T007 [P] Create console tier management component test file in `src/Elsa.Platform.Console/src/features/deployments/DeploymentTiersPanel.test.tsx`

---

## Phase 2: Foundational

**Purpose**: Core tier primitives and shared contract shape that block all user stories.

**Critical**: No user story work can begin until this phase is complete.

- [X] T008 Define `DeploymentTierStatus`, `DeploymentTierCapabilityCategory`, capability ID constants, and capability catalog records in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierModels.cs`
- [X] T009 Define tier definition, tier assignment, tier change record, tier impact summary, and tier environment sample records in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierModels.cs`
- [X] T010 Define create, update, archive, restore, and impact-preview request records in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierModels.cs`
- [X] T011 Define `IWorkspaceDeploymentTierStore` operations for capability catalog, tier list, create, update, archive, restore, impact preview, default seeding, and environment counts in `src/Elsa.Platform.Deployment.Core/Workspace/IWorkspaceDeploymentTierStore.cs`
- [X] T012 Implement platform-defined capability catalog and default Dev/Test/Stage/Production tier mappings in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierService.cs`
- [X] T013 Update deployment environment and cockpit tier response models to carry tier identity, label, status, and capabilities in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentModels.cs`
- [X] T014 Update cockpit environment summary tier shape in `src/Elsa.Platform.Deployment.Core/Cockpit/DeploymentCockpitModels.cs`
- [X] T015 [P] Add API DTOs for tier capabilities, tier definitions, tier mutations, impact preview, and tier-aware environment requests in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentContracts.cs`
- [X] T016 [P] Add TypeScript tier capability, tier definition, impact preview, and tier-aware environment model types in `src/Elsa.Platform.Console/src/features/deployments/deploymentModels.ts`
- [X] T017 Register deployment tier core service only, leaving concrete store wiring until persistence support exists in `src/Elsa.Platform.Api/Program.cs`

**Checkpoint**: Tier primitives, request/response models, and default semantic catalog are available for backend, API, and console work.

---

## Phase 3: User Story 1 - Configure Workspace Tiers (Priority: P1)

**Goal**: Workspace admins can create, view, edit, archive, and restore custom deployment tiers with platform-defined coded capabilities.

**Independent Test**: Create a workspace tier named `UAT`, assign pre-production and promotion capabilities, save it, and verify it appears in the workspace tier list while non-admin mutation is blocked.

### Tests for User Story 1

- [ ] T018 [P] [US1] Add core tests for capability catalog, duplicate active names, unknown capability rejection, and last-active-tier archive prevention in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentTierServiceTests.cs`
- [ ] T019 [P] [US1] Add persistence tests for tier create/update/archive/restore, capability assignments, environment counts, and audit records in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceTierPersistenceTests.cs`
- [ ] T020 [P] [US1] Add API tests for tier capability listing, tier CRUD, archive/restore, duplicate rejection, and non-admin mutation denial in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTierApiTests.cs`
- [ ] T021 [P] [US1] Add console tests for tier list, create form, edit form, capability selection, duplicate-name errors, archive/restore, and non-admin blocked state in `src/Elsa.Platform.Console/src/features/deployments/DeploymentTiersPanel.test.tsx`

### Implementation for User Story 1

- [X] T022 [US1] Implement tier validation, impact-preview generation, changed-safeguard summaries, create, update, archive, restore, and capability assignment orchestration in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierService.cs`
- [X] T023 [US1] Add tier entity classes for definitions, capability assignments, and change records in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [X] T024 [US1] Configure tier entity mappings, indexes, relationships, value conversions, and delete behavior in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T025 [US1] Implement tier store methods for list, create, update, archive, restore, environment count, audit persistence, and concrete store registration in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs` and `src/Elsa.Platform.Api/Program.cs`
- [X] T026 [US1] Add SQLite migration for deployment tier definitions, capability assignments, and change records in `src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/`
- [X] T027 [US1] Add SQL Server migration for deployment tier definitions, capability assignments, and change records in `src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [X] T028 [US1] Add tier capability and tier management endpoints under workspace deployment routes in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T029 [US1] Add deployment tier API client functions in `src/Elsa.Platform.Console/src/features/deployments/deploymentApi.ts`
- [X] T030 [US1] Implement admin tier management UI in `src/Elsa.Platform.Console/src/features/deployments/DeploymentTiersPanel.tsx`
- [X] T031 [US1] Integrate the tier management panel into the deployments page in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 1 is independently functional and testable through core tests, persistence tests, API tests, and the console tier management panel.

---

## Phase 4: User Story 2 - Attach Custom Tiers To Environments (Priority: P2)

**Goal**: Deployment environment create/edit flows require an active workspace tier and show the selected tier label and capabilities in environment summaries.

**Independent Test**: Create a tier named `Production EU`, assign production-like capabilities, attach it to an environment, and verify the cockpit shows the custom tier label and inherited capability IDs.

### Tests for User Story 2

- [ ] T032 [P] [US2] Add core tests for environment creation and update with tier IDs, archived-tier rejection, and cross-workspace tier rejection in `tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs`
- [ ] T033 [P] [US2] Add persistence tests for environment tier references, archived-tier readability, and active-tier reassignment in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs`
- [ ] T034 [P] [US2] Add API tests for environment create/update with `tierId`, archived-tier assignment rejection, and cockpit tier response shape in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentApiTests.cs`
- [ ] T035 [P] [US2] Add console tests for active tier selection, tier capability display, archived-tier display, and environment setup error states in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 2

- [X] T036 [US2] Replace environment create/update request tier enum usage with tier ID validation in `src/Elsa.Platform.Deployment.Core/Workspace/WorkspaceDeploymentService.cs`
- [X] T037 [US2] Add `TierId` and temporary legacy tier fields to deployment environment entity and projection code in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/DeploymentWorkspaceEntities.cs`
- [X] T038 [US2] Update deployment environment mapping and indexes for tier references in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T039 [US2] Update environment create/update persistence to validate same-workspace active tier selection and preserve archived-tier reads in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T040 [US2] Update cockpit projection to include tier identity, label, status, and capability IDs in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [X] T041 [US2] Update environment create/update endpoint request handling to accept `tierId` and transition fixed tier compatibility in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T042 [US2] Update deployment setup API client requests to send `tierId` in `src/Elsa.Platform.Console/src/features/deployments/deploymentApi.ts`
- [X] T043 [US2] Replace fixed tier dropdown with active workspace tier selector in `src/Elsa.Platform.Console/src/features/deployments/DeploymentSetupPanel.tsx`
- [X] T044 [US2] Update environment edit controls and cockpit display to show custom tier labels and archived status in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`

**Checkpoint**: User Story 2 is independently functional and testable through environment setup/editing and cockpit responses.

---

## Phase 5: User Story 3 - Preserve Stable Platform Semantics (Priority: P3)

**Goal**: Tier-aware behavior uses coded capabilities rather than tier names for deployment warnings, promotion eligibility, confirmation expectations, rollback availability, secret verification, and observability expectations.

**Independent Test**: Define two differently named tiers with the same production-like capabilities and verify both receive the same production-grade safeguards.

### Tests for User Story 3

- [X] T045 [P] [US3] Add core validation tests for promotion-source, promotion-target, production-like, confirmation-required, rollback-enabled, secret-verification-required, and observability-required capabilities in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentValidationServiceTests.cs`
- [ ] T046 [P] [US3] Add API tests proving promotion preview and run preparation use tier capabilities rather than tier names in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTierApiTests.cs`
- [ ] T047 [P] [US3] Add console tests for production-like warnings, promotion-target blockers, confirmation messaging, and rollback availability from capability IDs in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 3

- [X] T048 [US3] Add tier capability lookup helpers for deployment safeguards in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierService.cs`
- [X] T049 [US3] Update promotion preview validation to block invalid source or target tier capability combinations in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentValidationService.cs`
- [X] T050 [US3] Update deployment run validation to apply confirmation and rollback capability semantics in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentRunService.cs`
- [X] T051 [US3] Update API promotion preview and run endpoints to include tier capability validation failures in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T052 [US3] Update console promotion, deployment, and rollback affordances to use tier capability IDs from cockpit data in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [X] T053 [US3] Update run history and deployment warnings copy to reference tier labels while basing behavior on capability IDs in `src/Elsa.Platform.Console/src/features/deployments/DeploymentRunsPanel.tsx`

**Checkpoint**: User Story 3 is independently functional and proves semantics come from coded capabilities, not tier names.

---

## Phase 6: User Story 4 - Migrate Existing Fixed Tiers (Priority: P4)

**Goal**: Existing Dev, Test, Stage, and Production environments continue working through default tier definitions and migration mapping.

**Independent Test**: Open a workspace with existing fixed-tier environments and verify each environment is assigned to the equivalent default tier with preserved display meaning and behavior.

### Tests for User Story 4

- [ ] T054 [P] [US4] Add core tests for default tier seeding and Dev/Test/Stage/Production capability mappings in `tests/Elsa.Platform.Deployment.Core.Tests/DeploymentTierServiceTests.cs`
- [ ] T055 [P] [US4] Add persistence migration tests for mapping existing environment tier values to default tier records in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceTierPersistenceTests.cs`
- [ ] T056 [P] [US4] Add API tests proving workspaces without custom tiers receive defaults and legacy tier requests map during transition in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTierApiTests.cs`
- [ ] T057 [P] [US4] Add console tests proving default tiers appear in empty-state setup and migrated environments keep readable tier labels in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

### Implementation for User Story 4

- [X] T058 [US4] Implement idempotent default tier seeding for workspaces without tier definitions in `src/Elsa.Platform.Deployment.Core/Workspace/DeploymentTierService.cs`
- [X] T059 [US4] Implement persistence default seeding, legacy fixed-tier mapping, and admin-review flagging for fallback-mapped environments in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/DeploymentWorkspaceStore.cs`
- [ ] T060 [US4] Extend SQLite migrations to backfill default tier definitions and environment tier references in `src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/`
- [ ] T061 [US4] Extend SQL Server migrations to backfill default tier definitions and environment tier references in `src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [X] T062 [US4] Add compatibility mapping for old fixed tier request values during the transition period in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentEndpoints.cs`
- [X] T063 [US4] Update console empty-state setup to rely on server-provided default tiers in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.tsx`
- [ ] T064 [US4] Update TypeScript tests and fixtures to use tier definitions instead of fixed tier enum strings in `src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx`

**Checkpoint**: User Story 4 is independently functional and verifies existing workspaces remain operational after migration.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verification, cleanup, and documentation that affect multiple stories.

- [ ] T065 [P] Update quickstart verification results in `specs/025-custom-deployment-tiers/quickstart.md`
- [ ] T066 [P] Update API contract examples if final request or response names changed in `specs/025-custom-deployment-tiers/contracts/workspace-deployment-tiers-api.md`
- [ ] T067 [P] Update console UX contract notes if final tier management labels changed in `specs/025-custom-deployment-tiers/contracts/console-deployment-tiers-ux.md`
- [ ] T068 Run core tests with `dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter DeploymentTier`
- [ ] T069 Run persistence tests with `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceTier`
- [ ] T070 Run API tests with `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceDeploymentTier`
- [X] T071 Run console deployment tests with `cd src/Elsa.Platform.Console && npm test -- --run deployments`
- [X] T072 Run console typecheck with `cd src/Elsa.Platform.Console && npm run typecheck`
- [X] T073 Run whitespace validation with `git diff --check`
- [ ] T074 Add and run bounded-query verification for 20 tiers and 250 environments in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceTierPersistenceTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational; delivers the MVP tier-management slice.
- **User Story 2 (Phase 4)**: Depends on Foundational and can use US1 tier definitions when testing through the UI.
- **User Story 3 (Phase 5)**: Depends on Foundational and tier capability data from US1/US2.
- **User Story 4 (Phase 6)**: Depends on Foundational and persistence shape from US1/US2.
- **Polish (Phase 7)**: Depends on all desired stories being complete.

### User Story Dependencies

- **US1 Configure Workspace Tiers**: MVP and first recommended implementation target after Foundational.
- **US2 Attach Custom Tiers To Environments**: Can begin after Foundational, but full UI validation is easier after US1 exposes tier management.
- **US3 Preserve Stable Platform Semantics**: Requires tier capabilities and environment tier references.
- **US4 Migrate Existing Fixed Tiers**: Requires tier definitions, environment references, and default mappings.

### Parallel Opportunities

- Setup file creation tasks T004-T007 can run in parallel.
- Foundational DTO/model tasks T015-T016 can run in parallel after core model shape is agreed.
- Test tasks within each user story are parallelizable because they target different test projects.
- Persistence migration tasks for SQLite and SQL Server can run in parallel after entity mapping is defined.
- Console and API implementation tasks can run in parallel once shared contracts are stable.
- Polish documentation tasks T065-T067 can run in parallel.

---

## Parallel Example: User Story 1

```text
Task: "Add core tests for capability catalog, duplicate active names, unknown capability rejection, and last-active-tier archive prevention in tests/Elsa.Platform.Deployment.Core.Tests/DeploymentTierServiceTests.cs"
Task: "Add persistence tests for tier create/update/archive/restore, capability assignments, environment counts, and audit records in tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspaceTierPersistenceTests.cs"
Task: "Add API tests for tier capability listing, tier CRUD, archive/restore, duplicate rejection, and non-admin mutation denial in tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTierApiTests.cs"
Task: "Add console tests for tier list, create form, edit form, capability selection, duplicate-name errors, archive/restore, and non-admin blocked state in src/Elsa.Platform.Console/src/features/deployments/DeploymentTiersPanel.test.tsx"
```

## Parallel Example: User Story 2

```text
Task: "Add core tests for environment creation and update with tier IDs, archived-tier rejection, and cross-workspace tier rejection in tests/Elsa.Platform.Deployment.Core.Tests/WorkspaceDeploymentServiceTests.cs"
Task: "Add persistence tests for environment tier references, archived-tier readability, and active-tier reassignment in tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentWorkspacePersistenceTests.cs"
Task: "Add API tests for environment create/update with tierId, archived-tier assignment rejection, and cockpit tier response shape in tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentApiTests.cs"
Task: "Add console tests for active tier selection, tier capability display, archived-tier display, and environment setup error states in src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx"
```

## Parallel Example: User Story 3

```text
Task: "Add core validation tests for promotion-source, promotion-target, production-like, confirmation-required, rollback-enabled, secret-verification-required, and observability-required capabilities in tests/Elsa.Platform.Deployment.Core.Tests/DeploymentValidationServiceTests.cs"
Task: "Add API tests proving promotion preview and run preparation use tier capabilities rather than tier names in tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTierApiTests.cs"
Task: "Add console tests for production-like warnings, promotion-target blockers, confirmation messaging, and rollback availability from capability IDs in src/Elsa.Platform.Console/src/features/deployments/DeploymentsPage.test.tsx"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 for User Story 1.
3. Validate that workspace admins can manage tiers and non-admin users are blocked.
4. Stop and demo tier management before environment assignment work.

### Incremental Delivery

1. Deliver US1 tier management.
2. Deliver US2 environment tier assignment and cockpit display.
3. Deliver US3 coded-capability safeguards.
4. Deliver US4 migration/default-tier compatibility.
5. Run Phase 7 verification after each selected delivery boundary.

### Validation Notes

- Tests in each user story should be written before implementation and should fail before the corresponding implementation tasks.
- Keep mechanical migration work separate from behavior changes when practical.
- Preserve workspace isolation and fail closed on missing, archived, or cross-workspace tier references.
