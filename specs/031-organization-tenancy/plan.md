# Implementation Plan: Organization Tenancy

**Branch**: `031-organization-tenancy` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/031-organization-tenancy/spec.md`

## Summary

Introduce a first-class `Organization` aggregate above workspaces and move the customer tenant boundary from workspace to organization. Existing workspace-owned records remain workspace-owned, but every workspace belongs to one organization. Account identity resolves to organization memberships and workspace memberships; organization roles control customer-level administration, while workspace roles and permission grants continue controlling access to workspace resources. Existing workspaces migrate into organizations without changing workspace IDs or workspace-owned resource IDs.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core authentication/authorization, ASP.NET Core cookies, JWT bearer validation, EF Core, existing `Elsa.Platform.PackageCatalog.Core.Accounts` account/workspace services, existing workspace authorization helpers, React Router, TanStack Query, Vitest, Playwright where needed, xUnit, and FluentAssertions.

**Storage**: Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Organization tables store customer tenant records, memberships, entitlements, workspace ownership references, and safe audit metadata.

**Testing**: Focused `dotnet test` for PackageCatalog core/persistence and Platform API authorization tests; console `vitest` for organization/workspace context and selector behavior; `git diff --check`.

**Target Platform**: ASP.NET Core Platform API and React console served from the platform host.

**Project Type**: Modular monolith web service with React console and EF-backed workspace persistence.

**Performance Goals**: Customer context resolution for a typical account with 5 organizations and 25 total workspaces should complete with bounded queries and return in under 1 second in the integration test environment.

**Constraints**: Organization is the customer tenant boundary. Workspace remains the operational isolation boundary. Existing workspace resource IDs and authorization behavior must remain compatible during migration. Caller-supplied account, organization, workspace, role, or entitlement claims are not authority. Operator admin access remains separate. Runtime tenant overlays remain out of scope.

**Scale/Scope**: One platform API host, many organizations, many accounts per organization, many workspaces per organization, and existing workspace-owned catalog, runtime-builder, deployment, artifact, and console records.

## Constitution Check

- **Control Plane First**: Pass. This feature changes platform control-plane tenancy and does not reconcile workflow runtime data-plane state.
- **Bounded Subsystems**: Pass. Account, organization, and workspace tenancy live in PackageCatalog core/persistence and Platform API adapters. Deployment, Runtime Builder, and Artifact features consume organization-aware workspace authorization through contracts.
- **Contract Stability**: Pass with care. Existing workspace-scoped routes require compatibility behavior while organization-aware context and management routes are introduced.
- **Safety By Design**: Pass. Organization records store customer and audit metadata only; secrets, provider tokens, and deployment credentials remain outside organization records.
- **Incremental Verifiability**: Pass. Migration, context resolution, organization membership, workspace management, authorization isolation, and console selection can be tested independently.

## Project Structure

### Documentation (this feature)

```text
specs/031-organization-tenancy/
├── spec.md
├── amendment-plan.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── organization-workspace-api.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  Elsa.Platform.PackageCatalog.Core/
    Accounts/
      AccountModels.cs
      AccountWorkspaceService.cs
      OrganizationWorkspaceService.cs
      WorkspaceAuthorizationModels.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
    AccountWorkspaceStore.cs
    Models/CatalogModelConfiguration.cs

  Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/
    Migrations/

  Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/
    Migrations/

  Elsa.Platform.Api/
    Authentication/
      WorkspaceAccessResolver.cs
      WorkspaceAuthorization.cs
    Workspace/
      WorkspaceMeEndpoints.cs
      OrganizationWorkspaceEndpoints.cs

  Elsa.Platform.Console/
    src/
      lib/
        auth/
          authModels.ts
      features/
        organizations/
          organizationApi.ts
          organizationModels.ts
          OrganizationWorkspaceSwitcher.tsx
```

```text
tests/
  Elsa.Platform.PackageCatalog.Core.Tests/
    OrganizationWorkspaceServiceTests.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    OrganizationWorkspacePersistenceTests.cs

  Elsa.Platform.Api.Tests/
    OrganizationContextApiTests.cs
    OrganizationWorkspaceApiTests.cs
    OrganizationWorkspaceIsolationTests.cs

  Elsa.Platform.Console/
    src/features/organizations/OrganizationWorkspaceSwitcher.test.tsx
```

**Structure Decision**: Extend the existing account/workspace package in place because it already owns account identity, workspace memberships, and entitlement snapshots. Avoid a new tenancy subsystem until a second bounded context needs a package-level abstraction.

## Phase Plan

### Phase 1: Spec Alignment And Contracts

Outcome:

- New organization-tenancy spec package defines hierarchy, migration rules, data model, API contract, and cross-spec amendments.

Exit gate:

- No unresolved clarification markers; older workspace-tenant specs have forward-compatibility notes pointing to this feature.

### Phase 2: Organization Domain Foundation

Outcome:

- Account/workspace core models include organizations, organization memberships, organization roles, organization entitlements, and workspace organization ownership.

Exit gate:

- Core tests prove first sign-in creates organization plus default workspace, returning sign-in is idempotent, and organization membership does not imply workspace access.

### Phase 3: Persistence And Migration

Outcome:

- EF mappings and SQLite/SQL Server migrations add organization tables, workspace organization references, membership records, entitlement records, and backfill existing workspaces.

Exit gate:

- Persistence tests prove every workspace has one organization, existing workspace IDs remain stable, ownerless active workspaces are prevented, and duplicate workspace names are scoped to organization.

### Phase 4: API Authorization And Compatibility

Outcome:

- Customer context APIs return organization-aware data. Organization workspace management APIs are available. Existing workspace-scoped APIs resolve organization ownership and keep compatibility behavior.

Exit gate:

- API tests prove cross-organization denial, cross-workspace denial inside one organization, organization admin workspace management, missing entitlement fail-closed behavior, and operator/customer separation.

### Phase 5: Console Integration

Outcome:

- Console shows organization and workspace context, supports workspace selection inside organization, and removes "workspace tenant boundary" copy from user-facing deployment UI.

Exit gate:

- Console tests prove multi-organization/multi-workspace selection, access-denied states, and existing workspace feature navigation.

### Phase 6: Cleanup And Follow-On Deferrals

Outcome:

- Legacy `WorkspaceKind.Organization` semantics are deprecated or removed where safe. Future organization-shared assets are documented as separate specs.

Exit gate:

- No current spec or console copy presents workspace as the customer tenant boundary without the organization/workspace qualification.

## Complexity Tracking

No constitution violations are expected.
