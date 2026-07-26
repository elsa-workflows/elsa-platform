# Implementation Plan: Console Authentication

**Branch**: `004-admin-dashboard-auth` | **Date**: 2026-05-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-admin-dashboard-auth/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Protect the deployed console from anonymous access by adding a small app-owned cookie session flow backed by the existing configured admin API key. The ASP.NET Core host serves minimal login/logout endpoints, gates `/admin` dashboard assets before static file serving, authorizes admin REST APIs with either the existing API key header or the dashboard cookie, rejects cross-origin cookie-authenticated admin mutations, and throttles repeated failed dashboard logins in memory.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API host; existing React + TypeScript console remains a static asset build.

**Primary Dependencies**: ASP.NET Core authentication/authorization, cookie authentication, existing custom API key authentication handler, existing console build output.

**Storage**: Existing configuration secret for the admin API key; HTTP-only auth cookie for dashboard sessions; in-memory per-client failed-login throttle only. No new durable storage.

**Testing**: xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory integration tests.

**Target platform**: ASP.NET Core API container deployed to Azure App Service.

**Project Type**: Web service hosting REST APIs plus static console assets.

**Performance Goals**: Authentication, same-origin checks, and login throttle checks add negligible overhead to dashboard and admin API requests.

**Constraints**: Keep auth small; no OIDC, RBAC, user database, persistent lockout state, or frontend key storage in this feature. Dashboard sessions use 8-hour sliding expiration.

**Scale/Scope**: Internal console for a small number of operators. In-memory throttling is acceptable because failed-login state may reset on process restart.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The plan MUST answer these gates:

- **Manifest-first**: Not impacted. This feature only changes dashboard/admin API access control.
- **No arbitrary code execution**: Not impacted. No package processing paths change.
- **Stable contracts**: Not impacted. No `ValenceControl.PackageManifests` contracts change.
- **Schema evolution**: Not impacted. No manifest schema changes.
- **Immutable versions**: Not impacted. Package-version handling is unchanged.
- **Approval separation**: Preserved. This feature gates existing admin workflows without merging validation, approval, or listing concerns.
- **Explicit sources**: Not impacted. Source configuration behavior is unchanged.
- **Safe public API**: Preserved. Public endpoints remain anonymous and unchanged.
- **Debuggability**: Preserved. Dashboard inspectability remains available after login; failed login throttling is transient and does not create durable audit records in this MVP.
- **Modular monolith**: Pass. The design stays inside the existing ASP.NET Core host.
- **Runtime Builder readiness**: Not impacted. Existing catalog APIs remain available to authenticated console sessions.
- **Simplicity**: Pass. Uses existing framework authentication, the existing admin key, same-origin request checks, and in-memory throttling rather than new identity infrastructure.

## Project Structure

### Documentation (this feature)

```text
specs/004-admin-dashboard-auth/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── admin-dashboard-auth.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ValenceControl.Api/
│   ├── Authentication/
│   ├── Admin/
│   └── Program.cs
└── ValenceControl.Console/
    └── src/

tests/
└── ValenceControl.Api.Tests/
    └── AdminDashboardAuthenticationTests.cs
```

**Structure Decision**: Keep the implementation in the existing API host because it already owns admin API authentication and serves the built dashboard assets. Add focused integration tests in the API test project. Do not add a frontend login app because the login page must remain available before protected React assets are served.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
