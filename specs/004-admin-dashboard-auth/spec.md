# Feature Specification: Admin Dashboard Authentication

**Feature Branch**: `004-admin-dashboard-auth`

**Created**: 2026-05-15

**Status**: Draft

**Input**: User description: "The deployed admin dashboard is anonymously accessible. Proceed with the recommended option: app-owned cookie authentication for the dashboard using the existing configured admin API key, using Spec Kit."

## Clarifications

### Session 2026-05-16

- Q: What dashboard session lifetime should the app-owned cookie use? → A: 8-hour sliding session.
- Q: What CSRF protection should apply when dashboard cookies authorize admin API calls? → A: Same-origin checks for cookie-authenticated admin API mutations.
- Q: How should repeated failed dashboard login attempts be handled? → A: In-memory per-client throttling for repeated failed login attempts.
- Q: What failed-login threshold and retry delay should the dashboard throttle use? → A: 5 failures in 15 minutes, then 5-minute retry delay.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Require Admin Login For Dashboard (Priority: P1)

An administrator opening the admin dashboard must be prompted for an admin credential before any dashboard page or static dashboard asset can be used.

**Why this priority**: The dashboard exposes operational admin capabilities and must not be anonymously accessible.

**Independent Test**: Open a dashboard route without credentials and confirm the system does not serve the dashboard shell.

**Acceptance Scenarios**:

1. **Given** no authenticated admin session, **When** a user opens `/admin/overview`, **Then** the system redirects browser navigation to the admin login page.
2. **Given** no authenticated admin session, **When** a user requests dashboard static assets directly, **Then** the system does not serve those assets anonymously.
3. **Given** no authenticated admin session, **When** a user opens public catalog endpoints, **Then** those endpoints remain public.

---

### User Story 2 - Sign In With Existing Admin Key (Priority: P1)

An administrator can enter the existing configured admin API key once and receive a browser session for using the dashboard UI.

**Why this priority**: The fastest safe path reuses the existing production secret and avoids adding identity infrastructure before it is needed.

**Independent Test**: Submit the configured admin key to the login form and confirm subsequent dashboard and admin API requests succeed without exposing the key to frontend JavaScript.

**Acceptance Scenarios**:

1. **Given** the configured admin API key, **When** an administrator submits the login form, **Then** the system creates an authenticated dashboard session and returns the administrator to the requested admin page.
2. **Given** an invalid admin API key, **When** an administrator submits the login form, **Then** the system rejects the attempt and does not create a dashboard session.
3. **Given** a valid dashboard session, **When** the dashboard calls admin REST APIs, **Then** the calls are authorized by the session without requiring a JavaScript-configured API key.

---

### User Story 3 - Sign Out Of Dashboard Session (Priority: P2)

An administrator can explicitly end the dashboard browser session from a server endpoint.

**Why this priority**: Shared machines and operational workstations need a simple way to clear access.

**Independent Test**: Sign in, sign out, then confirm dashboard pages require login again.

**Acceptance Scenarios**:

1. **Given** a valid dashboard session, **When** an administrator signs out, **Then** the session cookie is cleared and the user is returned to the login page.
2. **Given** a signed-out browser, **When** the user revisits a dashboard route, **Then** login is required again.

### Edge Cases

- The admin API key is not configured: dashboard login attempts fail safely and no anonymous dashboard access is granted.
- Repeated failed login attempts from the same client are throttled in-memory after 5 failures in 15 minutes, returning a 5-minute retry delay without introducing persistent lockout state.
- Failed-login throttling identifies a client by the normalized remote IP address after trusted forwarded-header processing; if no trusted forwarded IP is available, the direct connection remote IP is used.
- A login request includes a non-admin or external return URL: the system redirects only to safe local admin paths.
- A non-browser client calls dashboard routes without credentials: the system returns an unauthorized response instead of serving dashboard content.
- Cookie-authenticated admin API mutation requests from cross-origin browser contexts are rejected; API-key header clients are unchanged.
- Existing machine clients using the admin API key header continue to work.
- Public package catalog endpoints remain anonymous.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST require authenticated admin access for all `/admin` dashboard pages and dashboard static assets except the login endpoint.
- **FR-002**: System MUST provide an admin login page that accepts the existing configured admin API key.
- **FR-003**: System MUST create an HTTP-only browser session after a successful admin key login.
- **FR-003a**: Dashboard browser sessions MUST use an 8-hour sliding expiration.
- **FR-004**: System MUST reject invalid or missing admin keys without creating a browser session.
- **FR-004a**: System MUST apply in-memory per-client throttling to repeated failed dashboard login attempts.
- **FR-004b**: System MUST throttle a client after 5 failed dashboard login attempts within 15 minutes and require a 5-minute retry delay before processing another login attempt from that client.
- **FR-005**: System MUST authorize admin REST API calls with either the existing admin API key header or the authenticated dashboard session.
- **FR-005a**: System MUST require same-origin request validation for cookie-authenticated admin API mutation requests while preserving existing API-key header clients.
- **FR-006**: System MUST preserve anonymous access to public catalog APIs and health endpoints.
- **FR-007**: System MUST provide a logout endpoint that clears the dashboard browser session.
- **FR-008**: System MUST prevent login return URLs from redirecting outside safe local admin routes.
- **FR-009**: System MUST avoid exposing the configured admin API key to generated frontend assets or browser-readable storage.
- **FR-010**: System MUST avoid introducing OIDC, RBAC, or a new user database for this feature.

### Key Entities *(include if feature involves data)*

- **Dashboard Session**: Browser authentication state created after a valid admin key login; contains only minimal admin identity claims and is carried by an HTTP-only cookie.
- **Admin Credential Submission**: Login form payload containing the admin key and optional return target; validated against existing server configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Anonymous requests to dashboard routes no longer receive the React dashboard shell or static dashboard assets.
- **SC-002**: An administrator with the configured admin key can sign in and load the dashboard in under one minute.
- **SC-003**: Existing admin API clients using the API key header continue to receive authorized responses.
- **SC-004**: Public catalog and health endpoints continue to respond without admin authentication.
- **SC-005**: Dashboard API calls work after login without embedding an admin key in frontend build-time configuration.

## Assumptions

- The existing admin API key remains the source of truth for MVP dashboard access.
- The dashboard and admin API are served by the same ASP.NET Core app origin in production.
- A simple server-rendered login page is acceptable for MVP and does not need to match the full React dashboard chrome.
- Future OIDC support may replace or augment this flow, but it is outside this feature.
