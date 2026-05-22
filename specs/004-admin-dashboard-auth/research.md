# Research: Console Authentication

## Decision: Reuse the existing admin API key to establish an HTTP-only dashboard cookie

**Rationale**: The production system already has an authenticated admin API backed by a configured API key. Reusing that secret lets the dashboard require auth immediately without adding a user database, OIDC setup, or role model.

**Alternatives considered**:

- **Keep only API key headers**: Rejected because the React dashboard would need the key in browser-readable configuration or manual request setup.
- **Put the dashboard behind platform auth only**: Useful later, but it would make local and container behavior diverge from the application security model.
- **Add OIDC now**: Rejected for MVP scope. It is the likely future long-term option, but not needed to stop anonymous access.

## Decision: Use an 8-hour sliding dashboard session

**Rationale**: An 8-hour sliding cookie matches a normal operator workday while keeping sessions bounded for shared operational machines. It is also simple to express through ASP.NET Core cookie authentication settings.

**Alternatives considered**:

- **Short 2-hour session**: Rejected because it increases friction during normal admin work without materially changing the MVP threat model.
- **12-hour or 24-hour session**: Rejected because it extends access beyond a normal workday for limited benefit.
- **Non-sliding fixed session**: Rejected because the current requirement favors continuous active work over forcing re-login during an active shift.

## Decision: Gate dashboard paths in the API host before serving static files

**Rationale**: The dashboard is deployed as static assets under `/admin`. `UseStaticFiles()` can serve files before endpoint authorization, so dashboard path access must be checked before static file serving.

**Alternatives considered**:

- **Protect only the fallback HTML endpoint**: Rejected because built JavaScript and CSS assets could still be fetched anonymously.
- **Move assets to a separate authenticated service**: Rejected as distributed infrastructure outside the current operational scope.

## Decision: Let admin API policy accept either API key or dashboard cookie

**Rationale**: Existing machine clients must keep using the API key header. Browser dashboard calls should use the HTTP-only cookie and avoid storing the key in frontend code.

**Alternatives considered**:

- **Require frontend API key env var**: Rejected because it exposes the credential to the browser bundle.
- **Create separate dashboard-only API endpoints**: Rejected as duplicate surface area.
- **Disallow cookies for admin REST APIs**: Rejected because the dashboard would need a browser-readable credential or duplicated backend surface.

## Decision: Require same-origin validation for cookie-authenticated admin API mutations

**Rationale**: Accepting cookies for admin REST APIs introduces CSRF risk for unsafe methods. Same-origin validation keeps existing API-key clients unchanged while rejecting cross-origin browser mutation attempts for cookie-authenticated requests.

**Alternatives considered**:

- **Rely only on `SameSite=Lax`**: Rejected because explicit server-side validation is clearer and more testable for admin mutations.
- **Anti-forgery token on all cookie-authenticated API requests**: Stronger, but rejected for MVP because same-origin checks cover the required threat reduction with less frontend/API plumbing.
- **Apply checks to API-key requests**: Rejected because machine clients using headers are not using ambient browser cookies and must remain unchanged.

## Decision: Keep login UI server-rendered and minimal

**Rationale**: The login page is a security boundary and should not require the protected React asset bundle to load. A small server-rendered form is enough for MVP.

**Alternatives considered**:

- **Build login into React app**: Rejected because loading the React app before authentication weakens the access boundary and complicates asset gating.

## Decision: Throttle repeated failed logins in memory per client

**Rationale**: The shared-key login endpoint should not allow unlimited guessing. In-memory per-client throttling after 5 failed attempts in 15 minutes with a 5-minute retry delay is measurable, testable, and avoids introducing persistent account or lockout state.

**Alternatives considered**:

- **No throttling**: Rejected because it leaves the login endpoint with unlimited online guessing attempts.
- **Persistent lockout state**: Rejected because this feature intentionally avoids new durable storage and user/account modeling.
- **Global shutdown after many failures**: Rejected because one noisy client could block legitimate operators.
- **Exponential backoff after every failure**: Rejected because it adds implementation complexity without a clearer MVP requirement.
