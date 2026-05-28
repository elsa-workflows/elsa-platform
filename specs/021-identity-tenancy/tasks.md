# Tasks: Identity And Workspace Tenancy

**Input**: Design documents from `specs/021-identity-tenancy/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included because this feature changes authentication, authorization, tenancy, and private-data visibility behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or does not depend on incomplete tasks.
- **[Story]**: User story label from `spec.md`.
- Every task includes exact file paths.

## Phase 1: Setup

**Purpose**: Prepare shared documentation and dependency surface for platform identity and workspace tenancy.

- [X] T001 Add JWT/OIDC authentication package version entry in `Directory.Packages.props`
- [X] T002 Add JWT/OIDC authentication package reference in `src/Elsa.Platform.Api/Elsa.Platform.Api.csproj`
- [X] T003 [P] Add identity and tenancy configuration notes to `specs/021-identity-tenancy/quickstart.md`
- [X] T004 [P] Review existing workspace endpoints and list authorization migration scope in `specs/021-identity-tenancy/research.md`

---

## Phase 2: Foundational

**Purpose**: Core authentication and authorization primitives that block all user stories.

**Checkpoint**: Shared customer identity resolution and workspace authorization shape is ready for user story work.

- [X] T005 [P] Add platform identity defaults and options in `src/Elsa.Platform.Api/Authentication/PlatformIdentityOptions.cs`
- [X] T006 [P] Add platform identity settings to `src/Elsa.Platform.Api/appsettings.json`
- [X] T007 [P] Add platform identity development settings to `src/Elsa.Platform.Api/appsettings.Development.json`
- [X] T008 Add platform OIDC/JWT authentication registration in `src/Elsa.Platform.Api/Program.cs`
- [X] T009 Add `PlatformIdentityReader` abstraction that derives `TrustedWorkspaceIdentity` from authenticated principals in `src/Elsa.Platform.Api/Authentication/PlatformIdentityReader.cs`
- [X] T010 Add test identity helper utilities in `tests/Elsa.Platform.Api.Tests/TestWorkspaceIdentity.cs`
- [X] T011 [P] Add shared workspace access result models in `src/Elsa.Platform.PackageCatalog.Core/Accounts/WorkspaceAuthorizationModels.cs`
- [X] T012 Add workspace access resolver service in `src/Elsa.Platform.Api/Authentication/WorkspaceAccessResolver.cs`
- [X] T013 Add workspace authorization endpoint filters/policies in `src/Elsa.Platform.Api/Authentication/WorkspaceAuthorization.cs`
- [X] T014 Register customer identity and workspace authorization services in `src/Elsa.Platform.Api/Program.cs`

---

## Phase 3: User Story 1 - Sign In With Trusted Identity (Priority: P1) MVP

**Goal**: A valid trusted identity maps to a platform-local account/workspace context, while missing or invalid identity is rejected.

**Independent Test**: Present a valid identity to `GET /api/me/workspaces`, verify account/workspace context is derived from trusted identity, and verify no caller-supplied user IDs are accepted.

### Tests for User Story 1

- [X] T015 [P] [US1] Add valid JWT-derived identity tests in `tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs`
- [X] T016 [P] [US1] Add invalid issuer, missing subject, expired token, and wrong audience tests in `tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs`
- [X] T017 [P] [US1] Add browser-supplied account/workspace/user ID rejection tests in `tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs`
- [X] T018 [P] [US1] Add profile metadata update tests in `tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs`
- [X] T019 [P] [US1] Add configurable claim mapping and provider preset tests in `tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs`

### Implementation for User Story 1

- [X] T020 [US1] Replace production use of `TrustedHeaderWorkspaceIdentityReader` with platform identity resolution in `src/Elsa.Platform.Api/Authentication/PlatformIdentityReader.cs`
- [X] T021 [US1] Keep trusted-header identity mode explicitly gated for local/test use in `src/Elsa.Platform.Api/Authentication/WorkspaceIdentity.cs`
- [X] T022 [US1] Update `GET /api/me/workspaces` to use platform identity resolution in `src/Elsa.Platform.Api/Workspace/WorkspaceMeEndpoints.cs`
- [X] T023 [US1] Ensure unauthorized workspace identity responses use consistent problem details in `src/Elsa.Platform.Api/Authentication/WorkspaceIdentity.cs`
- [X] T024 [US1] Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter PlatformIdentityTests`

**Checkpoint**: Platform identity is trusted only when derived from validated auth context, and `/api/me/workspaces` is independently usable.

---

## Phase 3a: Customer OIDC Console Login

**Goal**: Hosted console users can start customer login, carry a server-side customer session into workspace APIs, and sign out without using operator credentials as customer identity.

- [X] T024a Add customer OIDC client settings and OpenID Connect package reference in `Directory.Packages.props`, `src/Elsa.Platform.Api/Elsa.Platform.Api.csproj`, and `src/Elsa.Platform.Api/appsettings*.json`
- [X] T024b Add dedicated customer cookie/session defaults and session identity reader in `src/Elsa.Platform.Api/Authentication/`
- [X] T024c Add customer session, login, and logout endpoints in `src/Elsa.Platform.Api/Authentication/CustomerAuthEndpoints.cs`
- [X] T024d Allow configured customer sessions to serve the hosted console shell without authorizing operator-only APIs in `src/Elsa.Platform.Api/Authentication/AdminDashboardAuthenticationMiddleware.cs`
- [X] T024e Add same-origin protection for cookie-authenticated workspace mutations in `src/Elsa.Platform.Api/Authentication/AdminDashboardRequestForgeryGuard.cs`
- [X] T024f Add console auth API, provider, customer route guards, and cookie-aware API requests in `src/Elsa.Platform.Console/src/lib/auth/`, `src/Elsa.Platform.Console/src/app/routes.tsx`, `src/Elsa.Platform.Console/src/main.tsx`, and `src/Elsa.Platform.Console/src/lib/api/httpClient.ts`
- [X] T024g Add customer authentication API tests in `tests/Elsa.Platform.Api.Tests/CustomerAuthenticationTests.cs` and console HTTP client coverage in `src/Elsa.Platform.Console/src/lib/api/httpClient.test.ts`
- [X] T024h Run `npm test` and `npm run typecheck` in `src/Elsa.Platform.Console`

---

## Phase 4: User Story 2 - First Sign-In Creates A Personal Workspace (Priority: P1)

**Goal**: First sign-in provisions exactly one account, external identity, personal workspace, and owner membership.

**Independent Test**: Sign in twice and concurrently with the same trusted identity, then verify stable account/workspace records without duplicates.

### Tests for User Story 2

- [X] T025 [P] [US2] Add first sign-in provisioning tests in `tests/Elsa.Platform.Api.Tests/WorkspaceProvisioningTests.cs`
- [X] T026 [P] [US2] Add repeated sign-in idempotency tests in `tests/Elsa.Platform.Api.Tests/WorkspaceProvisioningTests.cs`
- [X] T027 [P] [US2] Add concurrent first sign-in tests in `tests/Elsa.Platform.Api.Tests/WorkspaceProvisioningTests.cs`
- [X] T028 [P] [US2] Add multi-workspace membership listing tests in `tests/Elsa.Platform.Api.Tests/WorkspaceProvisioningTests.cs`

### Implementation for User Story 2

- [X] T029 [US2] Harden `AccountWorkspaceService.GetOrCreateAsync` profile updates and duplicate handling in `src/Elsa.Platform.PackageCatalog.Core/Accounts/AccountWorkspaceService.cs`
- [X] T030 [US2] Ensure external identity uniqueness and workspace membership mappings remain enforced in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T031 [US2] Extend `AccountWorkspaceStore` queries to exclude soft-deleted workspaces from customer context in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/AccountWorkspaceStore.cs`
- [X] T032 [US2] Update workspace context response contracts if needed in `src/Elsa.Platform.Api/Workspace/WorkspaceContracts.cs`
- [X] T033 [US2] Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceProvisioningTests`

**Checkpoint**: First sign-in and returning sign-in are deterministic and safe under concurrency.

---

## Phase 5: User Story 3 - Enforce Workspace Authorization Everywhere (Priority: P1)

**Goal**: Every workspace-owned read/write path uses shared workspace authorization and blocks non-members, including callers who know IDs.

**Independent Test**: Seed two users in separate workspaces and prove each can access only their own workspace-owned records across current workspace APIs.

### Tests for User Story 3

- [X] T034 [P] [US3] Add cross-workspace source visibility tests in `tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs`
- [X] T035 [P] [US3] Add cross-workspace package browsing tests in `tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs`
- [X] T036 [P] [US3] Add cross-workspace builder endpoint tests in `tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs`
- [X] T037 [P] [US3] Add cross-workspace runtime configuration tests in `tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs`
- [X] T038 [P] [US3] Add anonymous public catalog regression tests in `tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs`

### Implementation for User Story 3

- [X] T039 [US3] Apply shared workspace authorization to source endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceSourceEndpoints.cs`
- [X] T040 [US3] Apply shared workspace authorization to package endpoints in `src/Elsa.Platform.Api/Workspace/WorkspacePackageEndpoints.cs`
- [X] T041 [US3] Apply shared workspace authorization to builder endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceBuilderEndpoints.cs`
- [X] T042 [US3] Apply shared workspace authorization to runtime configuration endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`
- [X] T043 [US3] Centralize workspace-visible public/private source query checks in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicSourceQueries.cs`
- [X] T044 [US3] Centralize workspace-visible package query checks in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs`
- [X] T045 [US3] Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceIsolationTests`

**Checkpoint**: Workspace authorization is consistent across current customer-owned API paths.

---

## Phase 6: User Story 4 - Use Role And Entitlement Boundaries (Priority: P2)

**Goal**: Workspace roles and entitlements are enforced server-side for privileged and paid operations.

**Independent Test**: Assign different roles and entitlement states, then verify privileged operations succeed or fail according to server records.

### Tests for User Story 4

- [X] T046 [P] [US4] Add owner/source-admin/reader source-management tests in `tests/Elsa.Platform.Api.Tests/WorkspaceAuthorizationTests.cs`
- [X] T047 [P] [US4] Add entitlement denial and limit tests in `tests/Elsa.Platform.Api.Tests/WorkspaceAuthorizationTests.cs`
- [X] T048 [P] [US4] Add current-membership-after-role-change tests in `tests/Elsa.Platform.Api.Tests/WorkspaceAuthorizationTests.cs`

### Implementation for User Story 4

- [X] T049 [US4] Extend workspace access models for operation-specific role checks in `src/Elsa.Platform.PackageCatalog.Core/Accounts/WorkspaceAuthorizationModels.cs`
- [X] T050 [US4] Enforce source administrator role through shared authorization in `src/Elsa.Platform.Api/Workspace/WorkspaceSourceEndpoints.cs`
- [X] T051 [US4] Enforce entitlement checks through shared authorization in `src/Elsa.Platform.PackageCatalog.Core/Accounts/WorkspaceSourceService.cs`
- [X] T052 [US4] Ensure admin entitlement updates remain operator-only in `src/Elsa.Platform.Api/Admin/Workspaces/AdminWorkspaceEntitlementEndpoints.cs`
- [X] T053 [US4] Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceAuthorizationTests`

**Checkpoint**: Roles and entitlements are authoritative server-side, independent of frontend state.

---

## Phase 7: User Story 5 - Preserve Operator Access Separately (Priority: P2)

**Goal**: Operator admin access remains available and does not become a customer identity or workspace membership.

**Independent Test**: Use operator authorization for operator-only work, verify customer identities cannot perform operator-only actions, and verify operator fallback does not provision customer workspace context.

### Tests for User Story 5

- [X] T054 [P] [US5] Add operator entitlement endpoint authorization tests in `tests/Elsa.Platform.Api.Tests/OperatorAuthorizationTests.cs`
- [X] T055 [P] [US5] Add customer-denied-from-operator-endpoints tests in `tests/Elsa.Platform.Api.Tests/OperatorAuthorizationTests.cs`
- [X] T056 [P] [US5] Add admin-key-does-not-provision-customer-context tests in `tests/Elsa.Platform.Api.Tests/OperatorAuthorizationTests.cs`

### Implementation for User Story 5

- [X] T057 [US5] Separate customer and operator authentication policy registration in `src/Elsa.Platform.Api/Authentication/AdminAuthorization.cs`
- [X] T058 [US5] Ensure admin dashboard cookie/API-key schemes do not satisfy customer workspace identity in `src/Elsa.Platform.Api/Authentication/PlatformIdentityReader.cs`
- [X] T059 [US5] Update admin dashboard auth documentation in `specs/004-admin-dashboard-auth/quickstart.md`
- [X] T060 [US5] Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter OperatorAuthorizationTests`

**Checkpoint**: Operator and customer identities are separate authorization paths.

---

## Phase 8: Polish And Verification

**Purpose**: Final hardening, documentation, and broad verification.

- [X] T061 [P] Update identity/tenancy API contract examples in `specs/021-identity-tenancy/contracts/identity-workspace-api.md`
- [X] T062 [P] Update appsettings production guidance in `src/Elsa.Platform.Api/appsettings.Production.json`
- [X] T063 Review security-sensitive logs and audit metadata in `src/Elsa.Platform.Api/Authentication/`
- [X] T064 Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj`
- [X] T065 Run `dotnet test Elsa.Platform.sln`
- [X] T066 Execute quickstart smoke checks from `specs/021-identity-tenancy/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **US1 Sign In With Trusted Identity (Phase 3)**: Depends on Foundational and is the MVP.
- **US2 First Sign-In Creates A Personal Workspace (Phase 4)**: Depends on US1 identity resolution but remains independently testable.
- **US3 Enforce Workspace Authorization Everywhere (Phase 5)**: Depends on Foundational and benefits from US1/US2 fixtures.
- **US4 Role And Entitlement Boundaries (Phase 6)**: Depends on US3 shared authorization.
- **US5 Preserve Operator Access Separately (Phase 7)**: Depends on customer/operator policy shape from US1 and US3.
- **Polish (Phase 8)**: Depends on all desired user stories.

### User Story Dependencies

- **US1 (P1)**: MVP customer identity path.
- **US2 (P1)**: Requires US1 identity source, then validates provisioning.
- **US3 (P1)**: Requires shared workspace authorization primitives and can proceed after Foundational if fixtures are available.
- **US4 (P2)**: Builds on US3 authorization.
- **US5 (P2)**: Can run after US1 policy separation, but final verification should happen after US3.

### Parallel Opportunities

- T003 and T004 can run in parallel.
- T005, T006, T007, and T011 can run in parallel.
- Test-writing tasks within each user story can run in parallel.
- US4 and US5 tests can be prepared in parallel after the foundational policy shape is stable.

## Parallel Examples

### User Story 1

```text
Task: "Add valid JWT-derived identity tests in tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs"
Task: "Add invalid issuer, missing subject, expired token, and wrong audience tests in tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs"
Task: "Add browser-supplied account/workspace/user ID rejection tests in tests/Elsa.Platform.Api.Tests/PlatformIdentityTests.cs"
```

### User Story 3

```text
Task: "Add cross-workspace source visibility tests in tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs"
Task: "Add cross-workspace package browsing tests in tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs"
Task: "Add cross-workspace builder endpoint tests in tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs"
Task: "Add cross-workspace runtime configuration tests in tests/Elsa.Platform.Api.Tests/WorkspaceIsolationTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1.
3. Validate `GET /api/me/workspaces` with valid and invalid trusted identity.
4. Stop and verify the customer identity path before broad endpoint migration.

### Incremental Delivery

1. Add customer identity resolution.
2. Harden account/workspace provisioning.
3. Migrate existing workspace endpoints to shared authorization.
4. Add role and entitlement enforcement.
5. Confirm operator fallback remains separate.

### Validation Focus

Security regressions are the main risk. The critical checks are invalid identity rejection, no browser-supplied authority, idempotent provisioning, cross-workspace non-disclosure, current role/entitlement enforcement, and operator/customer separation.
