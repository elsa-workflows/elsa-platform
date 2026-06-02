# Implementation Plan: Engine Health Verification

**Branch**: `023-engine-health-verification` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/023-engine-health-verification/spec.md`

## Summary

Add workspace-scoped engine verification and heartbeat metadata for deployment engine registrations. Registered engines continue to start safely as unreachable, but authorized users can manually verify reachability and trusted runtime callers can report heartbeat metadata. The platform persists health, version, certificate, credential verification, heartbeat time, verification time, and safe diagnostics; cockpit and console render those states and runtime controls remain blocked until server-side health, permission, capability, and confirmation gates pass.

This slice stays inside the deployment UX PRD: it updates persisted control-plane metadata only. It does not implement real deployment apply, runtime instance inspection, live drift detection, or telemetry provider integrations.

> **Forward compatibility note**: `specs/031-organization-tenancy` adds Organization as the customer tenant above the workspace/environment engine records described here.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permission grants, EF Core catalog persistence, `Elsa.Platform.Deployment.Core` workspace services, React Router, TanStack Query, Vitest, Playwright where needed, xUnit, and FluentAssertions.

**Storage**: Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Engine records gain verification metadata; optional append-only verification event records may be added if needed for audit/debugging.

**Testing**: Focused `dotnet test` for Deployment.Core, API, and persistence projects; `npm test -- --run deployments` and `npm run typecheck` for console; E2E smoke only if the console flow changes enough to need browser coverage.

**Target Platform**: ASP.NET Core Platform API and React console served from the platform host.

**Project Type**: Modular monolith web service with React admin/customer console and EF-backed persistence.

**Performance Goals**: Cockpit projection remains under the existing 3-second normal dataset target. Manual verification should complete within a bounded request timeout and never block cockpit reads. Heartbeat updates should be a single-engine metadata mutation.

**Constraints**: Workspace remains the engine/environment resource isolation boundary, and `specs/031-organization-tenancy` makes Organization the customer tenant boundary above it. Deployment core must not depend on API, UI, catalog persistence, hosting, or provider SDK internals. Raw secrets, provider tokens, and engine credentials must not appear in responses, console state, desired-state records, verification diagnostics, or audit/history records. Runtime controls fail closed while an engine is unreachable. Live deployment apply, runtime instance state, live observability pulls, and live drift detection remain out of scope.

**Scale/Scope**: One platform API host, many workspaces, and many workflow applications/environments/engines. This slice updates one engine per verification or heartbeat request and reuses the existing deployment cockpit response.

## Constitution Check

- **Control Plane First**: Pass. The feature stores and renders reachability/verification metadata for deployment control-plane records. It does not inspect workflow instances, bookmarks, queues, locks, logs, or runtime data-plane state.
- **Bounded Subsystems**: Pass. Deployment core owns verification models and service contracts. API, EF persistence, and console remain adapters. No dependency from deployment core to catalog persistence or API internals is introduced.
- **Contract Stability**: Pass. Engine verification and heartbeat routes are documented before implementation under `contracts/`.
- **Safety By Design**: Pass. Raw credentials are excluded from verification requests/responses and diagnostics. Runtime controls remain blocked when engine health is unsafe.
- **Incremental Verifiability**: Pass. Manual verification, heartbeat updates, cockpit projection, console states, and runtime-control gating are independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/023-engine-health-verification/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── engine-health-api.md
│   └── console-engine-health-ux.md
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
      WorkspaceDeploymentModels.cs
      WorkspaceDeploymentService.cs
      IWorkspaceDeploymentStore.cs
      EngineHealthService.cs
      EngineHealthModels.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
    DeploymentWorkspaceStore.cs
    Models/
      DeploymentWorkspaceEntities.cs
      CatalogModelConfiguration.cs

  Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/
    Migrations/

  Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/
    Migrations/

  Elsa.Platform.Api/
    Workspace/
      WorkspaceDeploymentEndpoints.cs
      WorkspaceDeploymentContracts.cs

  Elsa.Platform.Console/
    src/features/deployments/
      deploymentApi.ts
      deploymentModels.ts
      DeploymentsPage.tsx
      DeploymentsPage.test.tsx
      RuntimeControlsPanel.tsx
```

```text
tests/
  Elsa.Platform.Deployment.Core.Tests/
    EngineHealthServiceTests.cs
    RuntimeControlServiceTests.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    DeploymentWorkspacePersistenceTests.cs

  Elsa.Platform.Api.Tests/
    WorkspaceDeploymentEngineHealthTests.cs

  src/Elsa.Platform.Console/src/features/deployments/
    DeploymentsPage.test.tsx
```

**Structure Decision**: Keep verification orchestration in `Elsa.Platform.Deployment.Core` as deployment domain logic. Reuse the catalog EF database as the workspace-owned persistence adapter because engine registrations already live there. Keep API routes as authorization/contract adapters and add console behavior within the existing deployments feature directory.

## Phase Plan

### Phase 1: Verification And Heartbeat Domain

Outcome:

- Engine health request/result models, verification service, heartbeat service path, and server-side health classification exist.

Exit gate:

- Core tests prove healthy/degraded/unreachable classification, stale heartbeat rejection, capability preservation, and safe diagnostic behavior.

### Phase 2: Persistence And Contracts

Outcome:

- Engine verification metadata is persisted, projected into cockpit responses, and documented through API/console contracts.

Exit gate:

- Persistence tests prove metadata round-trips, raw credentials are absent, and cross-workspace updates are rejected.

### Phase 3: Authorized API Routes

Outcome:

- Manual verification and heartbeat endpoints enforce workspace membership, permissions, and target ownership.

Exit gate:

- API tests prove permission denial, cross-workspace denial, successful verification, failed verification, heartbeat freshness, and no secret leakage.

### Phase 4: Console UX

Outcome:

- Deployments console shows verification state, safe diagnostics, manual Verify action, pending/success/failure states, and refreshed runtime-control availability.

Exit gate:

- Console tests prove unverified/unreachable/healthy/degraded states, Verify action calls live API, cockpit refreshes, and controls remain disabled until health gates pass.

### Phase 5: Verification

Outcome:

- Quickstart results are recorded and all feature tasks are complete.

Exit gate:

- Focused backend tests, console typecheck, deployment console tests, and `git diff --check` pass.
