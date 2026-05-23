# Quickstart: Console Authentication

## Local Verification

1. Start the API host with `Authentication:ApiKey` configured.
2. Open `/admin/overview` without credentials.
3. Confirm the request redirects to `/admin/login`.
4. Request a dashboard static asset without credentials and confirm it is not served anonymously.
5. Submit an invalid key and confirm login is rejected without a session cookie.
6. Submit 5 invalid keys from the same client inside 15 minutes and confirm further attempts are throttled with a 5-minute retry delay.
7. Submit the configured admin key and confirm the browser redirects to `/admin/overview`.
8. Confirm the issued dashboard session is HTTP-only and configured for 8-hour sliding expiration.
9. Confirm dashboard admin API calls succeed using the session cookie.
10. Submit a cross-origin cookie-authenticated admin API mutation and confirm it is rejected by same-origin validation.
11. Call `/api/admin/sources` with the `X-Api-Key` header and confirm existing API clients still work.
12. Open `/health` and a public catalog endpoint without credentials and confirm they remain public.
13. Submit `/admin/logout` and confirm `/admin/overview` requires login again.
14. If customer OIDC login is configured, open a customer workspace console route without credentials and confirm browser navigation starts `/api/auth/login` instead of treating the admin API key as customer identity.

## Automated Verification

Run:

```sh
dotnet test tests/Elsa.Platform.PackageCatalog.Api.Tests/Elsa.Platform.PackageCatalog.Api.Tests.csproj
```

Expected coverage includes:

- Anonymous dashboard route and static asset blocking.
- Login success and failure.
- 8-hour sliding cookie configuration.
- Per-client failed-login throttling threshold and retry delay.
- Cookie-authorized admin API calls.
- Same-origin rejection for cookie-authenticated admin API mutations.
- Existing API-key header authorization.
- Logout.
- Public endpoint access.
- Separation between operator dashboard authentication and customer platform identity.

## Deployment Smoke

After deployment:

1. `GET https://<app>/admin/overview` should redirect to `/admin/login`.
2. `GET https://<app>/admin/assets/<asset>` without a cookie should not return the asset.
3. Repeated invalid login attempts should be throttled after the configured threshold.
4. A valid dashboard login should set an HTTP-only auth cookie.
5. Cookie-authenticated cross-origin admin API mutations should fail.
6. `GET https://<app>/health` should return `200 OK`.
7. Customer OIDC sessions, when enabled, should not authorize `/api/admin/*` endpoints.
