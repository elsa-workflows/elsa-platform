# Tasks: Account-Owned Custom Feeds

**Input**: Design documents from `/specs/008-account-custom-feeds/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included for each user story because this feature changes security-sensitive identity, entitlement, and visibility behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish feature files and shared contracts.

- [X] T001 Update active plan reference and feature metadata in AGENTS.md and .specify/feature.json
- [X] T002 [P] Add account/workspace API contract documentation in specs/008-account-custom-feeds/contracts/workspace-custom-feeds-api.md
- [X] T003 [P] Add account/workspace data model documentation in specs/008-account-custom-feeds/data-model.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core model and persistence primitives required by all user stories.

- [X] T004 Add Account, ExternalIdentity, Workspace, WorkspaceMembership, and WorkspaceEntitlementSnapshot domain models in src/Elsa.Platform.PackageCatalog.Core/Accounts/AccountModels.cs
- [X] T005 Extend PackageSource ownership fields in src/Elsa.Platform.PackageCatalog.Core/Packages/PackageModels.cs
- [X] T006 Add account/workspace DbSet properties in src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/CatalogDbContext.cs
- [X] T007 Configure account/workspace EF mappings and PackageSource ownership mapping in src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs
- [X] T008 Add SQLite migration for account-owned custom feeds in src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/
- [X] T009 Add SQL Server migration for account-owned custom feeds in src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/
- [X] T010 [P] Add account/workspace persistence coverage through tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs
- [X] T011 Register account/workspace services and query adapters in src/Elsa.Platform.PackageCatalog.Api/Program.cs

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Signed-In User Gets A Catalog Workspace (Priority: P1) MVP

**Goal**: Trusted external identities provision and retrieve catalog accounts and personal workspaces.

**Independent Test**: Call `GET /api/me/workspaces` twice with the same trusted identity and verify stable account/workspace IDs; call without trusted identity and verify rejection.

### Tests for User Story 1

- [X] T012 [P] [US1] Add API tests for workspace provisioning and unauthorized identity rejection in tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs
- [X] T013 [P] [US1] Add idempotent identity provisioning coverage through tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs

### Implementation for User Story 1

- [X] T014 [US1] Implement trusted workspace identity adapter in src/Elsa.Platform.PackageCatalog.Api/Authentication/WorkspaceIdentity.cs
- [X] T015 [US1] Implement account provisioning service contracts in src/Elsa.Platform.PackageCatalog.Core/Accounts/AccountWorkspaceService.cs
- [X] T016 [US1] Implement EF account workspace store in src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/AccountWorkspaceStore.cs
- [X] T017 [US1] Add `GET /api/me/workspaces` endpoint in src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceMeEndpoints.cs

**Checkpoint**: User Story 1 is independently functional.

---

## Phase 4: User Story 2 - Entitled User Adds A Custom Feed Source (Priority: P1)

**Goal**: Entitled workspace source administrators can create private workspace-owned NuGet sources.

**Independent Test**: Grant entitlement, create a workspace source, verify it is private/workspace-owned, and verify creation fails without entitlement or above source limit.

### Tests for User Story 2

- [X] T018 [P] [US2] Add API tests for entitlement grant and source creation in tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs
- [X] T019 [P] [US2] Add entitlement enforcement coverage through tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs

### Implementation for User Story 2

- [X] T020 [US2] Implement workspace entitlement and source creation service in src/Elsa.Platform.PackageCatalog.Core/Accounts/WorkspaceSourceService.cs
- [X] T021 [US2] Extend AccountWorkspaceStore with entitlement and workspace source operations in src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/AccountWorkspaceStore.cs
- [X] T022 [US2] Add admin entitlement endpoint in src/Elsa.Platform.PackageCatalog.Api/Admin/Workspaces/AdminWorkspaceEntitlementEndpoints.cs
- [X] T023 [US2] Add `GET` and `POST /api/workspaces/{workspaceId}/sources` endpoints in src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceSourceEndpoints.cs

**Checkpoint**: User Story 2 is independently functional.

---

## Phase 5: User Story 3 - Workspace Member Browses Public And Private Indexed Sources Together (Priority: P2)

**Goal**: Workspace members can browse selected public and workspace-owned indexed sources without leaking private data to anonymous or non-member callers.

**Independent Test**: Seed a workspace-owned source with packages, verify workspace member package/source queries include it, and verify anonymous public queries do not.

### Tests for User Story 3

- [X] T024 [P] [US3] Add API tests for workspace source/package visibility in tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs
- [X] T025 [P] [US3] Add workspace-visible package query coverage through tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs

### Implementation for User Story 3

- [X] T026 [US3] Extend public source queries with workspace visibility in src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicSourceQueries.cs
- [X] T027 [US3] Extend public catalog queries with workspace visibility in src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs
- [X] T028 [US3] Add workspace package endpoints in src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspacePackageEndpoints.cs
- [X] T029 [US3] Add workspace builder/compatibility endpoint wrappers or visibility plumbing in src/Elsa.Platform.PackageCatalog.Api/Workspace/

**Checkpoint**: User Story 3 is independently functional.

---

## Phase 6: User Story 4 - Operator Seeds Or Updates Workspace Entitlements (Priority: P3)

**Goal**: Operators can update entitlement snapshots before billing exists.

**Independent Test**: Use the admin endpoint to grant and revoke custom-source capability and verify subsequent workspace source creation honors the latest snapshot.

### Tests for User Story 4

- [X] T030 [P] [US4] Add admin entitlement update tests in tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceCustomFeedsApiTests.cs

### Implementation for User Story 4

- [X] T031 [US4] Complete entitlement response contracts and validation in src/Elsa.Platform.PackageCatalog.Api/Admin/Workspaces/AdminWorkspaceEntitlementEndpoints.cs
- [X] T032 [US4] Add entitlement quickstart coverage in specs/008-account-custom-feeds/quickstart.md

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validation, cleanup, and documentation alignment.

- [X] T033 Update AGENTS.md active technologies and recent changes for account-owned custom feeds
- [X] T034 Review source URL sanitization paths for workspace responses in src/Elsa.Platform.PackageCatalog.Api/Workspace/
- [X] T035 Run `dotnet build Elsa.Platform.sln --no-restore`
- [X] T036 Run `dotnet test Elsa.Platform.sln --no-build`
- [X] T037 Verify quickstart scenarios against implemented endpoints

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all stories.
- **US1 (Phase 3)**: Depends on Foundational.
- **US2 (Phase 4)**: Depends on US1 identity/workspace provisioning.
- **US3 (Phase 5)**: Depends on US1 membership and US2 source ownership.
- **US4 (Phase 6)**: Can be completed after US1 but is most useful with US2.
- **Polish (Phase 7)**: Depends on implemented story scope.

### Parallel Opportunities

- T002 and T003 can run in parallel.
- T010 can be written while model/mapping tasks are in progress.
- T012 and T013 can run in parallel.
- T018 and T019 can run in parallel.
- T024 and T025 can run in parallel.

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 to establish trusted identity and personal workspaces.
3. Complete US2 to create entitled workspace-owned sources.
4. Validate that anonymous users still only see public browseable sources.

### Incremental Delivery

1. Ship identity provisioning.
2. Add entitlement-gated source creation.
3. Add workspace-visible package browsing.
4. Add broader builder/compatibility workspace wrappers once package browsing is proven.
