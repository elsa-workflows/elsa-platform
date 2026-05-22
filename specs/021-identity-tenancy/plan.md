# Implementation Plan: Identity And Workspace Tenancy

**Branch**: `codex/021-identity-tenancy` | **Date**: 2026-05-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/021-identity-tenancy/spec.md`

## Summary

Promote the existing account/workspace foundation into the platform tenant model by adding a pluggable platform identity layer, deriving account/workspace context from trusted identity, centralizing workspace authorization, and preserving the current admin key flow as operator-only fallback. The first implementation should reuse the `Account`, `ExternalIdentity`, `Workspace`, `WorkspaceMembership`, and entitlement model from account-owned custom feeds, replace production use of trusted browser headers with a generic OIDC/JWT adapter plus provider presets/configuration, and harden workspace-scoped endpoint access with shared authorization helpers and cross-workspace tests.

## Technical Context

**Language/Version**: C# on .NET 10.

**Primary Dependencies**: ASP.NET Core authentication/authorization, ASP.NET Core cookies, JWT bearer validation, provider-neutral platform identity adapters, EF Core, existing `Elsa.Platform.PackageCatalog.*` account/workspace services, xUnit and FluentAssertions for tests.

**Storage**: Existing catalog EF Core stores and migrations for accounts, external identities, workspaces, memberships, entitlements, and workspace-owned resources.

**Testing**: `dotnet test`, with focused API and persistence coverage under `tests/Elsa.Platform.PackageCatalog.Api.Tests` and related package catalog test projects.

**Target Platform**: ASP.NET Core Package Catalog API and admin UI served from the platform host.

**Project Type**: Web service with React admin UI shell and EF-backed persistence.

**Performance Goals**: Authentication and workspace context resolution should not require more than one account/workspace lookup per request path that needs customer context; public anonymous catalog endpoints keep existing anonymous behavior and cacheability.

**Constraints**: Customer identity must come from a configured platform identity adapter that verifies tokens or trusted server-to-server context, never from browser-supplied IDs. Workspace is the platform tenant boundary. Existing operator admin access must remain separate. Runtime tenant overlays and first-class tenant reconciliation are out of scope.

**Scale/Scope**: One platform API host, many accounts, many workspaces per account, and all existing workspace-owned catalog and builder records. Organization workspace lifecycle, invitations, billing checkout, and deployment tenant overlays are deferred.

## Constitution Check

- **Control Plane First**: Pass. This feature governs platform control-plane access and explicitly defers runtime data-plane tenant reconciliation.
- **Bounded Subsystems**: Pass. Identity and workspace tenancy are implemented through Package Catalog API/Core/Persistence boundaries first; Deployment and Runtime Builder consume workspace authorization through API/service contracts rather than catalog persistence internals.
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
  Elsa.Platform.PackageCatalog.Api/
    Authentication/
      PlatformIdentityOptions.cs
      PlatformIdentityReader.cs
      WorkspaceAuthorization.cs
      WorkspaceAccessResolver.cs
    Workspace/
      WorkspaceMeEndpoints.cs
      Workspace*Endpoints.cs
    Program.cs
    appsettings*.json

  Elsa.Platform.PackageCatalog.Core/
    Accounts/
      AccountModels.cs
      AccountWorkspaceService.cs
      WorkspaceAuthorizationModels.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
    AccountWorkspaceStore.cs
    Models/CatalogModelConfiguration.cs

tests/
  Elsa.Platform.PackageCatalog.Api.Tests/
    PlatformIdentityTests.cs
    WorkspaceAuthorizationTests.cs
    WorkspaceIsolationTests.cs
```

**Structure Decision**: Evolve the existing Package Catalog account/workspace implementation in place. Do not add a second identity package until another subsystem needs a package-level abstraction; keep the first implementation behind shared API/Core services that later Deployment, Runtime Builder, BYOC, and managed hosting endpoints can reuse.

## Phase 0 Research

See [research.md](./research.md).

## Phase 1 Design

See [data-model.md](./data-model.md), [contracts/identity-workspace-api.md](./contracts/identity-workspace-api.md), and [quickstart.md](./quickstart.md).

## Complexity Tracking

No constitution violations are expected. The feature consolidates existing account/workspace work instead of creating a competing tenant model.
