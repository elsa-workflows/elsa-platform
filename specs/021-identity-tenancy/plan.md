# Implementation Plan: Identity And Workspace Tenancy

**Branch**: `codex/021-identity-tenancy` | **Date**: 2026-05-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/021-identity-tenancy/spec.md`

## Summary

Promote the existing account/workspace foundation into the platform tenant model by adding a pluggable Valence Control identity layer, deriving account/workspace context from trusted identity, centralizing workspace authorization, and preserving the current admin key flow as operator-only fallback. The first implementation should reuse the `Account`, `ExternalIdentity`, `Workspace`, `WorkspaceMembership`, and entitlement model from account-owned custom feeds, replace production use of trusted browser headers with a generic OIDC/JWT adapter plus provider presets/configuration, add an end-to-end customer login path for the React console, and harden workspace-scoped endpoint access with shared authorization helpers and cross-workspace tests.

> **Forward compatibility note**: `specs/031-organization-tenancy` amends this plan by adding a root `Organization` tenant above workspaces. Existing workspace authorization remains the resource isolation layer.

## Technical Context

**Language/Version**: C# on .NET 10.

**Primary Dependencies**: ASP.NET Core authentication/authorization, ASP.NET Core cookies, JWT bearer validation, OIDC authorization-code/PKCE or backend-for-frontend session support, provider-neutral Valence Control identity adapters, React console auth state integration, EF Core, existing `ValenceControl.PackageCatalog.*` account/workspace services, xUnit, Vitest, Playwright, and FluentAssertions for tests.

**Storage**: Existing catalog EF Core stores and migrations for accounts, external identities, workspaces, memberships, entitlements, and workspace-owned resources.

**Testing**: `dotnet test`, with focused API and persistence coverage under `tests/ValenceControl.Api.Tests` and related package catalog test projects.

**Target platform**: ASP.NET Core Valence Control API and React console served from the platform host.

**Project Type**: Web service with React admin UI shell and EF-backed persistence.

**Performance Goals**: Authentication and workspace context resolution should not require more than one account/workspace lookup per request path that needs customer context; public anonymous catalog endpoints keep existing anonymous behavior and cacheability.

**Constraints**: Customer identity must come from a configured Valence Control identity adapter that verifies tokens, OIDC callbacks, backend-mediated sessions, or trusted server-to-server context, never from browser-supplied IDs. Provider secrets must not be exposed to the browser. Workspace authorization remains the resource isolation layer from this slice; `specs/031-organization-tenancy` supersedes the root customer tenant boundary with Organization. Existing operator admin access must remain separate. Runtime tenant overlays and first-class tenant reconciliation are out of scope.

**Scale/Scope**: One platform API host, many accounts, many workspaces per account, and all existing workspace-owned catalog and builder records. Organization workspace lifecycle, invitations, billing checkout, and deployment tenant overlays are deferred.

## Constitution Check

- **Control Plane First**: Pass. This feature governs platform control-plane access and explicitly defers runtime data-plane tenant reconciliation.
- **Bounded Subsystems**: Pass. Identity and workspace tenancy are implemented through Valence Control API/Core/Persistence boundaries first; Deployment and Runtime Builder consume workspace authorization through API/service contracts rather than catalog persistence internals.
- **Contract Stability**: Pass with care. New authentication and workspace context contracts must be documented before replacing trusted-header behavior.
- **Safety By Design**: Pass. Caller-supplied account, role, entitlement, and workspace membership claims are not trusted; server-side records remain authoritative.
- **Incremental Verifiability**: Pass. Customer auth, account provisioning, workspace authorization, operator fallback, and cross-workspace isolation are independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/021-identity-tenancy/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── identity-workspace-api.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ValenceControl.Api/
    Authentication/
      ControlIdentityOptions.cs
      ControlIdentityReader.cs
      WorkspaceAuthorization.cs
      WorkspaceAccessResolver.cs
    Workspace/
      WorkspaceMeEndpoints.cs
      Workspace*Endpoints.cs
    Program.cs
    appsettings*.json

  ValenceControl.PackageCatalog.Core/
    Accounts/
      AccountModels.cs
      AccountWorkspaceService.cs
      WorkspaceAuthorizationModels.cs

  ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
    AccountWorkspaceStore.cs
    Models/CatalogModelConfiguration.cs

  ValenceControl.Console/
    src/
      app/
        routes.tsx
      lib/
        api/httpClient.ts
        auth/
          authApi.ts
          AuthProvider.tsx
          authModels.ts
      components/
        auth/
          SignInPage.tsx
          AuthCallbackPage.tsx

tests/
  ValenceControl.Api.Tests/
    ControlIdentityTests.cs
    WorkspaceAuthorizationTests.cs
    WorkspaceIsolationTests.cs
  ValenceControl.Console/
    src/lib/auth/*.test.tsx
    src/lib/api/httpClient.test.ts
  ValenceControl.Console.E2E/
    oidc-login.spec.ts
```

**Structure Decision**: Evolve the existing Package Catalog account/workspace implementation in place. Do not add a second identity package until another subsystem needs a package-level abstraction; keep the first implementation behind shared API/Core services that later Deployment, Runtime Builder, BYOC, and managed hosting endpoints can reuse.

## OIDC Integration Plan

### Target Authentication Shape

Use two compatible customer identity entry points that converge on the same `TrustedWorkspaceIdentity` contract:

1. **Console login session**: Browser users start a standards-based OIDC authorization-code flow with PKCE or a backend-for-frontend flow. The platform verifies the callback or token exchange, establishes customer-authenticated state, and exposes workspace APIs through that state.
2. **API bearer token**: API clients can send validated JWT bearer tokens directly to workspace endpoints. This path remains useful for service integration, tests, and deployments where an external frontend already owns login.

Both paths must resolve a verified issuer and subject, then use server-side account, workspace, role, and entitlement records as authority. Neither path may trust frontend-supplied user IDs, workspace IDs, roles, or entitlement claims.

### Provider Configuration

Extend `Authentication:ControlIdentity` from JWT validation into a complete customer identity configuration surface:

- `Provider`: `GenericOidc`, `MicrosoftEntra`, `Auth0`, `Keycloak`, or `Custom`.
- `Authority`: OIDC discovery authority.
- `Issuer`: explicit issuer override when discovery does not match deployment needs.
- `Audience`: expected API audience.
- `ClientId`: public or confidential OIDC client identifier.
- `ClientSecret`: optional confidential-client secret, stored only server-side.
- `Scopes`: default to `openid profile email` plus deployment-specific API scopes.
- `RedirectUri`: platform callback URI registered with the provider.
- `PostLogoutRedirectUri`: console return URI after sign-out.
- `RequireHttpsMetadata`: true outside local/test environments.
- `Claims`: subject, display name, and email claim mapping overrides.

Provider presets should set safe claim defaults and documentation examples only. The account/workspace model remains provider-neutral and always keys external identity by normalized issuer plus subject.

### Session And Browser Security

For the hosted React console, prefer a backend-for-frontend session cookie over browser-owned long-lived provider tokens:

- Use a dedicated customer session cookie separate from the existing operator/admin dashboard cookie.
- Mark customer cookies `HttpOnly`, `Secure` outside local/test, and `SameSite=Lax` or stricter where callback behavior allows it.
- Protect state-changing customer APIs with the same CSRF posture as any cookie-authenticated browser API.
- Keep provider refresh tokens and client secrets server-side only.
- Treat session status responses as non-secret UX hints; backend authorization remains the source of truth for every workspace API call.

### Backend Work Stream

1. Keep the existing `ControlOidcJwt` JWT bearer validation for API clients and token-accepting deployments.
2. Add customer login endpoints or route handlers for sign-in, callback, session status, and sign-out if the backend-for-frontend session pattern is selected.
3. Add an auth/session reader that converts the verified login session into the same `TrustedWorkspaceIdentity` used by bearer tokens.
4. Add configuration validation at startup or first use so missing authority, audience, redirect URI, client ID, or required claim mappings fail closed with operator-readable diagnostics.
5. Add provider preset helpers for Microsoft Entra, Auth0, Keycloak, and generic OIDC claim defaults without changing the persistence model.
6. Keep trusted-header identity mode local/test-only and ensure any bearer token or login session failure prevents fallback to trusted browser headers.
7. Keep operator admin cookie/API-key schemes separate from customer login schemes and ensure operator auth does not satisfy customer workspace identity.

### Frontend Work Stream

1. Add a console auth module that loads non-secret customer login status/configuration from the platform.
2. Add sign-in, callback handling, silent/session refresh behavior if supported by the chosen flow, and sign-out UI paths.
3. Update the shared HTTP client so workspace API calls include customer auth through either secure same-origin session credentials or bearer tokens.
4. Add route guards for customer-only console areas that redirect to sign-in or render an access state when identity is missing or expired.
5. Preserve anonymous public catalog browsing where supported, and only require customer login for workspace-owned features.
6. Ensure token/session errors clear unusable auth state and do not retry with forged identity headers.

### Testing Strategy

- API tests cover valid provider identity, wrong issuer, wrong audience, expired token, missing subject, invalid callback state, failed code exchange, missing provider config, and provider claim mapping overrides.
- Workspace tests prove first sign-in provisioning remains idempotent for both bearer-token and session-based identities.
- Operator tests prove admin API-key/cookie access does not create or satisfy customer workspace identity.
- Console unit tests cover login status loading, unauthorized route states, auth attachment in the HTTP client, logout clearing state, and workspace context reload after sign-in.
- E2E smoke tests cover configured local OIDC/test-provider login through the console, callback completion, `GET /api/me/workspaces`, and sign-out.

### Implementation Preference

Prefer a backend-for-frontend session for the hosted React console unless a deployment explicitly requires direct SPA bearer-token ownership. It keeps provider secrets and token refresh mechanics server-side, reduces browser token exposure, and still allows API clients to use direct bearer JWT validation. The plan must support both patterns at the identity boundary, but the first console implementation should choose one primary path to avoid duplicate login UX.

## Phase 0 Research

See [research.md](./research.md).

## Phase 1 Design

See [data-model.md](./data-model.md), [contracts/identity-workspace-api.md](./contracts/identity-workspace-api.md), and [quickstart.md](./quickstart.md).

## Complexity Tracking

No constitution violations are expected. The feature consolidates existing account/workspace work instead of creating a competing tenant model.
