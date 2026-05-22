# Contract: Console Authentication

## GET /admin/login

Returns a minimal HTML login form.

Query parameters:

- `returnUrl` optional local admin path.

Expected behavior:

- Anonymous callers receive `200 OK` with login form HTML.
- Already-authenticated dashboard sessions redirect to the safe `returnUrl` or `/admin/overview`.

## POST /admin/login

Accepts form data:

- `apiKey`: required admin key.
- `returnUrl`: optional local admin path.

Expected behavior:

- Valid key: `302 Found` to safe `returnUrl` or `/admin/overview`, with an HTTP-only dashboard auth cookie using 8-hour sliding expiration.
- Invalid key: `401 Unauthorized` with login form HTML and no session cookie.
- Missing configured server key: `401 Unauthorized` and no session cookie.
- Repeated failed attempts from the same client: after 5 failures in 15 minutes, return a throttled response with a 5-minute retry delay and no persistent lockout state.
- Unsafe `returnUrl`: ignored in favor of `/admin/overview`.

## POST /admin/logout

Expected behavior:

- Clears dashboard auth cookie.
- Redirects to `/admin/login`.

## GET /admin/{path}

Expected behavior:

- Authenticated dashboard session or valid API key: serves dashboard content.
- Anonymous browser navigation: redirects to `/admin/login?returnUrl=/admin/{path}`.
- Anonymous non-HTML/static requests: returns `401 Unauthorized`.

## /api/admin/*

Expected behavior:

- Valid `X-Api-Key` header: authorized, unchanged from current behavior.
- Valid dashboard auth cookie: authorized.
- Cookie-authenticated mutation requests must pass a same-origin browser request check; cross-origin mutation attempts are rejected without affecting API-key header clients.
- Anonymous request: `401 Unauthorized`.

Mutation methods:

- `POST`
- `PUT`
- `PATCH`
- `DELETE`

Same-origin validation applies only when the request is authenticated by the dashboard cookie and uses one of the mutation methods above.

Same-origin validation rules:

- Requests with a valid `X-Api-Key` header are not subject to this browser-origin check.
- If `Origin` is present, it must match the effective request scheme, host, and port.
- If `Origin` is absent, `Referer` must be present and match the effective request scheme, host, and port.
- If neither `Origin` nor `Referer` is present, reject the cookie-authenticated mutation request.
- Effective request scheme and host use ASP.NET Core forwarded-header processing when trusted proxy headers are configured.

## Public Endpoints

`/health`, `/`, `/api/packages`, `/api/features`, and compatibility/public package routes remain anonymous.
