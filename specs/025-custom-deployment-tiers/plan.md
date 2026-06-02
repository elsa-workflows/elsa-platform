# Implementation Plan: Custom Deployment Tiers

**Branch**: `025-custom-deployment-tiers` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/025-custom-deployment-tiers/spec.md`

## Summary

Replace the fixed deployment environment tier enum with workspace-owned tier definitions that have user-defined labels and platform-defined coded capabilities. The first slice keeps current deployment behavior intact by seeding equivalent default tiers, migrating existing Dev/Test/Stage/Production environment assignments to tier definitions, and exposing tier name plus capability semantics to the cockpit, environment setup, promotion preview, and deployment safeguards.

Deployment Core owns the tier domain model and capability semantics. The existing catalog EF persistence adapter stores workspace-owned tier definitions and assignments. The Platform API exposes authorized tier-management and environment-assignment routes. The console adds tier management for workspace admins and switches deployment environment setup/editing from fixed enum choices to active workspace tiers.

> **Forward compatibility note**: `specs/031-organization-tenancy` adds Organization above Workspace. This plan remains scoped to workspace-owned tier definitions and does not introduce organization-shared tier catalogs.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permissions, EF Core catalog persistence, `Elsa.Platform.Deployment.Core` workspace services, React Router, TanStack Query, Vitest, Playwright where needed, xUnit, and FluentAssertions.

**Storage**: Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Deployment tier tables store workspace-owned tier definitions, capability assignments, environment references, and safe audit metadata only.

**Testing**: Focused `dotnet test` for Deployment.Core, PackageCatalog persistence, and API tests; console `vitest` and typecheck for deployment tier management and environment setup; `git diff --check`.

**Target Platform**: ASP.NET Core Platform API and React console served from the platform host.

**Project Type**: Modular monolith web service with React console and EF-backed workspace persistence.

**Performance Goals**: Cockpit and tier settings load for a normal workspace with 20 tiers, 250 environments, and existing deployment cockpit data should complete with bounded queries and return in under 3 seconds in the integration test environment.

**Constraints**: Workspace remains the tier and environment resource isolation boundary, and `specs/031-organization-tenancy` makes Organization the customer tenant boundary above it. Deployment core remains persistence- and hosting-free. Tier capabilities are stable platform-defined semantics, not workspace-created strings. Existing deployment environments, history, promotion previews, and cockpit visibility must remain readable during migration. No raw secrets, provider tokens, or engine credentials may appear in tier records, responses, or audit entries.

**Scale/Scope**: Workspace-level tier definition and assignment model for many workspaces and many environments. External approval systems, organization-wide shared tier catalogs, per-tenant runtime overlays, and advanced policy authoring remain deferred.

## Constitution Check

- **Control Plane First**: Pass. The feature models deployment control-plane metadata and policy semantics only; it does not reconcile workflow instances, bookmarks, queues, logs, or transient runtime state.
- **Bounded Subsystems**: Pass. `Elsa.Platform.Deployment.Core` owns deployment tier contracts and semantics. API, EF persistence, migrations, and console remain adapters. Deployment does not depend on catalog persistence internals.
- **Contract Stability**: Pass. Workspace tier API and console contracts are documented under `contracts/` before implementation. The fixed enum is migrated through explicit compatibility behavior.
- **Safety By Design**: Pass. Tier data contains safe metadata and coded capability IDs only. Tier-aware safeguards fail closed when tier assignment, capability lookup, permission, or workspace isolation checks fail.
- **Incremental Verifiability**: Pass. Default seeding, tier management, environment assignment, cockpit projection, semantic capability use, migration, and console flows can be tested independently.

## Project Structure

### Documentation (this feature)

```text
specs/025-custom-deployment-tiers/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── workspace-deployment-tiers-api.md
│   └── console-deployment-tiers-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  Elsa.Platform.Deployment.Core/
    Cockpit/
      DeploymentCockpitModels.cs
    Workspace/
      DeploymentTierModels.cs
      DeploymentTierService.cs
      IWorkspaceDeploymentTierStore.cs
      WorkspaceDeploymentModels.cs
      WorkspaceDeploymentService.cs
      DeploymentValidationService.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
    DeploymentWorkspaceStore.cs
    Models/DeploymentWorkspaceEntities.cs
    Models/CatalogModelConfiguration.cs

  Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/
    Migrations/

  Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/
    Migrations/

  Elsa.Platform.Api/
    Workspace/
      WorkspaceDeploymentContracts.cs
      WorkspaceDeploymentEndpoints.cs

  Elsa.Platform.Console/
    src/
      features/deployments/
        deploymentApi.ts
        deploymentModels.ts
        DeploymentsPage.tsx
        DeploymentSetupPanel.tsx
        DeploymentTiersPanel.tsx
        DeploymentTiersPanel.test.tsx
```

```text
tests/
  Elsa.Platform.Deployment.Core.Tests/
    DeploymentTierServiceTests.cs
    WorkspaceDeploymentServiceTests.cs
    DeploymentValidationServiceTests.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    DeploymentWorkspaceTierPersistenceTests.cs
    DeploymentWorkspacePersistenceTests.cs

  Elsa.Platform.Api.Tests/
    WorkspaceDeploymentTierApiTests.cs
    WorkspaceDeploymentApiTests.cs
    WorkspaceDeploymentPermissionTests.cs
    WorkspaceDeploymentIsolationTests.cs

  Elsa.Platform.Console/
    src/features/deployments/DeploymentTiersPanel.test.tsx
    src/features/deployments/DeploymentsPage.test.tsx

  Elsa.Platform.Console.E2E/
    deployments.spec.ts
```

**Structure Decision**: Keep tier semantics in Deployment Core because they are deployment-domain policy inputs. Store definitions in the existing catalog workspace database because deployment environment records already live there. Extend existing deployment API and console feature areas to avoid creating a separate subsystem for one deployment setup concern.

## Phase Plan

### Phase 1: Planning And Contracts

Outcome:

- Spec, plan, research, data model, API contract, console contract, and quickstart define configurable tiers with coded capabilities and migration expectations.

Exit gate:

- Plan artifacts contain no unresolved clarifications and pass constitution checks.

### Phase 2: Tier Domain Foundation

Outcome:

- Deployment Core contains tier definition, capability, assignment, default catalog, validation, and impact-summary models/services.

Exit gate:

- Core tests prove duplicate active names fail, required capabilities are platform-defined, last active tier cannot be archived, referenced tiers cannot be hard-deleted, and default tier mappings preserve current semantics.

### Phase 3: Persistence And Migration

Outcome:

- EF entities, mappings, migrations, and store operations persist tier definitions, capability assignments, environment tier references, and tier change records. Existing fixed tier values migrate to default tier records.

Exit gate:

- Persistence tests prove workspace isolation, default seeding, migration mapping, archived-tier behavior, environment references, and audit records.

### Phase 4: API Integration

Outcome:

- Authorized workspace endpoints expose tier catalog, tier CRUD/archive, impact preview, and environment assignment using tier IDs while retaining compatibility for current clients during transition.

Exit gate:

- API tests prove permission enforcement, cross-workspace denial, duplicate validation, archived-tier assignment rejection, impact summaries, and cockpit response shape.

### Phase 5: Console Integration

Outcome:

- Deployment settings expose a tier management view, and environment setup/editing selects active workspace tiers instead of fixed enum values.

Exit gate:

- Console tests prove tier CRUD states, capability selection, impact warnings, environment assignment, archived-tier display, and permission-blocked behavior.

### Phase 6: Tier-Aware Safeguards And Verification

Outcome:

- Promotion preview, deployment warnings, rollback availability, confirmation messaging, and cockpit summaries use coded capabilities instead of tier labels wherever tier meaning matters.

Exit gate:

- Focused backend and console verification proves two differently named tiers with the same capabilities receive the same safeguards, while tiers without required capabilities fail closed.

## Complexity Tracking

No constitution violations are expected.
