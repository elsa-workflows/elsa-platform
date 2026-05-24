# Tasks: Console Authentication

**Input**: Design documents from `/specs/004-admin-dashboard-auth/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Integration tests are required because this feature changes authentication, routing, cookie behavior, CSRF protection, and login throttling.

**Organization**: Tasks are grouped by user story to preserve independently testable increments.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on incomplete tasks in the same phase
- **[Story]**: Maps to a user story from [spec.md](./spec.md)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the existing API host remains the dashboard and admin API authentication boundary.

- [X] T001 Review current middleware ordering and authentication registration in `src/Elsa.Platform.Api/Program.cs`
- [X] T002 [P] Review existing admin authentication helpers in `src/Elsa.Platform.Api/Authentication/`
- [X] T003 [P] Review existing dashboard authentication integration tests in `tests/Elsa.Platform.Api.Tests/AdminDashboardAuthenticationTests.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared auth primitives required by all dashboard authentication stories.

**CRITICAL**: No user story work should begin until this phase is complete.

- [X] T004 Add or update dashboard cookie auth defaults, including 8-hour sliding expiration constants, in `src/Elsa.Platform.Api/Authentication/AdminDashboardAuthenticationDefaults.cs`
- [X] T005 Add or update reusable admin API key validation in `src/Elsa.Platform.Api/Authentication/AdminApiKeyValidator.cs`
- [X] T006 Add or update authenticated admin principal creation for API key and dashboard cookie identities in `src/Elsa.Platform.Api/Authentication/AdminPrincipalFactory.cs`
- [X] T007 Update admin authorization policy to accept API key or dashboard cookie schemes in `src/Elsa.Platform.Api/Authentication/AdminAuthorization.cs`
- [X] T008 Register dashboard cookie authentication, shared auth services, and middleware ordering in `src/Elsa.Platform.Api/Program.cs`

**Checkpoint**: Auth primitives are ready and can be used by dashboard route gating, login, API authorization, and logout.

---

## Phase 3: User Story 1 - Require Admin Login For Dashboard (Priority: P1) MVP

**Goal**: Anonymous users cannot load the dashboard shell or dashboard static assets, while public endpoints remain anonymous.

**Independent Test**: Anonymous `/admin/overview` redirects to login, anonymous dashboard asset requests are not served, and `/health` plus public catalog endpoints remain public.

### Tests for User Story 1

- [X] T009 [US1] Add or update anonymous dashboard route, dashboard asset, non-browser unauthorized, health endpoint, and public catalog endpoint tests in `tests/Elsa.Platform.Api.Tests/AdminDashboardAuthenticationTests.cs`

### Implementation for User Story 1

- [X] T010 [US1] Implement or update dashboard path authorization middleware in `src/Elsa.Platform.Api/Authentication/AdminDashboardAuthenticationMiddleware.cs`
- [X] T011 [US1] Ensure dashboard gating runs before static file serving and admin fallback routing in `src/Elsa.Platform.Api/Program.cs`

**Checkpoint**: Anonymous dashboard access is blocked while public endpoints remain available.

---

## Phase 4: User Story 2 - Sign In With Existing Admin Key (Priority: P1)

**Goal**: Admins can sign in with the configured admin key, receive an HTTP-only 8-hour sliding session, use admin APIs via cookie auth, avoid browser-readable API keys, reject cross-origin cookie-authenticated mutations, and throttle repeated failed login attempts.

**Independent Test**: Valid login creates the configured session and authorizes admin API requests; invalid login does not create a session; repeated invalid attempts are throttled after 5 failures in 15 minutes with a 5-minute retry delay; cross-origin cookie-authenticated admin API mutations fail; API-key header clients continue to work.

### Tests for User Story 2

- [X] T012 [US2] Add or update valid login, invalid login, missing configured key, safe return URL, unsafe return URL, cookie attributes, and cookie-authorized admin API tests in `tests/Elsa.Platform.Api.Tests/AdminDashboardAuthenticationTests.cs`
- [X] T013 [US2] Add or update same-origin validation tests for Origin match, Referer fallback, missing Origin/Referer rejection, forwarded-host behavior, and API-key header bypass behavior in `tests/Elsa.Platform.Api.Tests/AdminDashboardAuthenticationTests.cs`
- [X] T014 [US2] Add failed-login throttle tests for 5 failures in 15 minutes, 5-minute retry delay, successful-login reset, process-local behavior, and remote-IP client key behavior in `tests/Elsa.Platform.Api.Tests/AdminDashboardAuthenticationTests.cs`

### Implementation for User Story 2

- [X] T015 [US2] Implement or update the server-rendered login endpoint and safe return URL handling in `src/Elsa.Platform.Api/Authentication/AdminDashboardAuthEndpoints.cs`
- [X] T016 [US2] Configure dashboard cookie options for HTTP-only storage and 8-hour sliding expiration in `src/Elsa.Platform.Api/Program.cs`
- [X] T017 [US2] Implement in-memory per-client failed-login throttling keyed by normalized remote IP in `src/Elsa.Platform.Api/Authentication/AdminDashboardLoginThrottle.cs`
- [X] T018 [US2] Integrate failed-login throttling into login submission handling in `src/Elsa.Platform.Api/Authentication/AdminDashboardAuthEndpoints.cs`
- [X] T019 [US2] Implement or update same-origin validation for cookie-authenticated admin API mutations using Origin first, Referer fallback, and effective scheme/host comparison in `src/Elsa.Platform.Api/Authentication/AdminDashboardRequestForgeryGuard.cs`
- [X] T020 [US2] Wire same-origin validation into the admin API request pipeline without affecting API-key header clients in `src/Elsa.Platform.Api/Program.cs`

**Checkpoint**: Dashboard session login works, API-key clients still work, failed login attempts are throttled, and cookie-authenticated mutation requests enforce same-origin validation.

---

## Phase 5: User Story 3 - Sign Out Of Dashboard Session (Priority: P2)

**Goal**: Admins can explicitly clear the dashboard browser session.

**Independent Test**: Sign in, sign out, then confirm dashboard routes require login again and the user is returned to the login page.

### Tests for User Story 3

- [X] T021 [US3] Add or update logout clearing and post-logout dashboard access tests in `tests/Elsa.Platform.Api.Tests/AdminDashboardAuthenticationTests.cs`

### Implementation for User Story 3

- [X] T022 [US3] Implement or update logout endpoint behavior in `src/Elsa.Platform.Api/Authentication/AdminDashboardAuthEndpoints.cs`
- [X] T023 [US3] Ensure logout route mapping is registered with the dashboard auth endpoints in `src/Elsa.Platform.Api/Program.cs`

**Checkpoint**: Login and logout lifecycle works end to end.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate the completed security behavior and keep documentation aligned.

- [X] T024 [P] Update auth quickstart notes after implementation in `specs/004-admin-dashboard-auth/quickstart.md`
- [X] T025 Run `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj`
- [X] T026 Run smoke checks from `specs/004-admin-dashboard-auth/quickstart.md`
- [X] T027 Review new auth helpers against simplicity and no-new-durable-storage constraints in `src/Elsa.Platform.Api/Authentication/`
- [X] T028 Confirm generated frontend assets and browser-readable storage do not contain the admin API key in `src/Elsa.Platform.Console/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **US1 Dashboard Gating (Phase 3)**: Depends on Foundational and is the MVP security closure.
- **US2 Login, Session, API Cookie Auth, CSRF, Throttle (Phase 4)**: Depends on Foundational and should follow US1 so authenticated browser use is restored after anonymous access is blocked.
- **US3 Logout (Phase 5)**: Depends on US2 because it clears the dashboard session created by login.
- **Polish (Phase 6)**: Depends on selected user stories.

### User Story Dependencies

- **US1 (P1)**: Independent after Foundation.
- **US2 (P1)**: Independent after Foundation, but should be integrated after US1 to keep dashboard gating closed.
- **US3 (P2)**: Depends on US2.

### Parallel Opportunities

- T002 and T003 can run in parallel during setup.
- Tests T012, T013, and T014 can be drafted together but touch the same test file, so coordinate edits.
- T017 and T019 can be implemented in parallel because they create separate auth helpers.
- T024 can run in parallel with final code review once behavior is stable.

## Parallel Example: User Story 2

```text
Task: "Implement in-memory per-client failed-login throttling in src/Elsa.Platform.Api/Authentication/AdminDashboardLoginThrottle.cs"
Task: "Implement or update same-origin validation for cookie-authenticated admin API mutations in src/Elsa.Platform.Api/Authentication/AdminDashboardRequestForgeryGuard.cs"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete US1 to stop anonymous dashboard shell and asset access.
3. Validate US1 independently.

### Incremental Delivery

1. Add US1 dashboard gating and verify public endpoints remain public.
2. Add US2 login/session/API-cookie behavior, same-origin mutation validation, and login throttling.
3. Add US3 logout behavior.
4. Run full API authentication tests and quickstart smoke checks.

### Notes

- Tests should be added or updated before implementation changes when behavior is not already covered.
- Same-origin validation applies to cookie-authenticated admin API mutation methods only.
- API-key header clients must remain compatible.
- Failed-login throttle state is intentionally process-local and non-durable.
