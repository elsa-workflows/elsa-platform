# Implementation Plan: Deployment UX

**Branch**: `022-deployment-ux` | **Date**: 2026-05-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/022-deployment-ux/spec.md`

## Summary

Replace the seeded deployment cockpit with durable workspace deployment functionality and live console workflows. The implementation adds workspace-owned deployment records for workflow applications, environments, engine registrations, structured desired-state revisions, validation snapshots, durable queued deployment runs, history, drift/observability metadata, capability-gated runtime controls, and explicit confirmation for risky actions. Deployment authorization is based on flexible workspace permission grants that can be composed into roles rather than hard-coded role checks.

The API exposes authorized workspace routes for setup, cockpit, promotion preview, deploy, rollback, and controls. The existing deployment contracts and engine packages remain dependency-light sibling subsystems; API, persistence, worker, and console adapters compose them through service contracts.

> **Forward compatibility note**: `specs/031-organization-tenancy` places these workspace-owned deployment records under an owning Organization tenant. Workspace remains the deployment resource isolation boundary.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence/worker; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, ASP.NET Core hosted services for the first in-process queue worker, existing workspace identity, new workspace permission grants, EF Core catalog persistence, `ElsaControl.Deployment.Abstractions`, `ElsaControl.Deployment.Engine`, React Router, TanStack Query, Vitest, Playwright where needed, xUnit and its built-in assertions.

**Storage**: Existing catalog relational database through `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Deployment tables store provider-backed secret references only, structured desired-state records, permission grants, confirmation metadata, queued run state, and append-only history.

**Testing**: Focused `dotnet test` for API, Deployment.Core, and persistence projects; `npm test` and `npm run typecheck` for console; E2E smoke coverage after the first full console flow is available.

**Target platform**: ASP.NET Core Elsa Control API and React console served from the platform host.

**Project Type**: Modular monolith web service with React admin/customer console, EF-backed persistence, and an in-process deployment queue worker for the first slice.

**Performance Goals**: Cockpit load for a normal workspace with up to 25 applications, 100 environments, 200 engines, 250 recent history events, observability metadata, and drift metadata should complete with bounded database queries and return in under 3 seconds in the integration test environment. Queue polling must avoid hot loops and process at most one active run per target environment at a time.

**Constraints**: Workspace remains the deployment resource isolation boundary, and `specs/031-organization-tenancy` makes Organization the customer tenant boundary above it. Deployment engine/core packages must not depend on API, UI, catalog persistence, hosting, or provider SDK internals. Raw secrets, provider tokens, and engine credentials must not appear in customer responses, desired-state records, console state, or audit/history records. Only one active deployment run may mutate a target environment at a time. Runtime workflow instance state and live telemetry provider queries are out of scope.

**Scale/Scope**: First durable workspace deployment slice for one platform API host, many workspaces, and many workflow applications/environments per workspace. Provider-specific cloud adapters, GitOps/OCI promotion, manifest/artifact import/export, signatures, external approval workflows, live observability pulls, and runtime tenant overlays remain deferred.

## Constitution Check

- **Control Plane First**: Pass. The feature stores and reconciles desired control-plane state, deployment runs, validation, drift metadata, observability metadata, and capability-gated controls; it does not reconcile workflow instances, bookmarks, execution state, queues, locks, or other runtime data-plane state.
- **Bounded Subsystems**: Pass. `ElsaControl.Deployment.Core` owns deployment workspace services, permission abstractions, queue coordination, and domain models. API, EF persistence, worker hosting, and console are adapters. Existing deployment engine/manifest/artifact packages remain dependency-light siblings.
- **Contract Stability**: Pass. New workspace API and console contracts are documented under `contracts/` before implementation. Names are still pre-public but are treated as stable within this feature.
- **Safety By Design**: Pass. Raw credentials are excluded from records and responses; deployment, rollback, and runtime control mutations fail closed when validation, permission, confirmation, or capability checks fail.
- **Incremental Verifiability**: Pass. Permissions, cockpit persistence, engine registration, promotion preview, queued runs, confirmation, runtime controls, and console flows are independently testable and represented as separate task phases.

## Project Structure

### Documentation (this feature)

```text
specs/022-deployment-ux/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── workspace-deployment-api.md
│   └── console-deployments-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ElsaControl.Deployment.Core/
    Cockpit/
      DeploymentCockpitModels.cs
      DeploymentCockpitService.cs
    Workspace/
      WorkspaceDeploymentModels.cs
      WorkspaceDeploymentService.cs
      IWorkspaceDeploymentStore.cs
      WorkspacePermissionModels.cs
      WorkspacePermissionService.cs
      DesiredStateModels.cs
      DeploymentValidationService.cs
      DeploymentRunService.cs
      DeploymentQueueWorker.cs
      RuntimeControlService.cs
      ConfirmationService.cs
      ObservabilityDriftService.cs

  ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/
    DeploymentWorkspaceStore.cs
    Models/CatalogModelConfiguration.cs

  ElsaControl.PackageCatalog.Persistence.SqliteMigrations/
    Migrations/

  ElsaControl.PackageCatalog.Persistence.SqlServerMigrations/
    Migrations/

  ElsaControl.Api/
    Workspace/
      WorkspaceDeploymentEndpoints.cs
      WorkspaceDeploymentContracts.cs
    Program.cs

  ElsaControl.Console/
    src/
      features/deployments/
        deploymentApi.ts
        deploymentModels.ts
        DeploymentsPage.tsx
        DeploymentsPage.test.tsx
        DeploymentSetupPanel.tsx
        PromotionPreviewPanel.tsx
        DeploymentRunsPanel.tsx
        RuntimeControlsPanel.tsx
```

```text
tests/
  ElsaControl.Deployment.Core.Tests/
    WorkspacePermissionServiceTests.cs
    WorkspaceDeploymentServiceTests.cs
    DeploymentValidationServiceTests.cs
    DeploymentRunServiceTests.cs
    DeploymentQueueWorkerTests.cs
    ConfirmationServiceTests.cs
    RuntimeControlServiceTests.cs

  ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    DeploymentWorkspacePersistenceTests.cs

  ElsaControl.Api.Tests/
    WorkspaceDeploymentApiTests.cs
    WorkspaceDeploymentIsolationTests.cs
    WorkspaceDeploymentPermissionTests.cs
    WorkspaceDeploymentMutationAuthorizationTests.cs

  ElsaControl.Console/
    src/features/deployments/*.test.tsx

  ElsaControl.Console.E2E/
    deployments.spec.ts
```

**Structure Decision**: Put deployment workspace orchestration in `ElsaControl.Deployment.Core` because it is product deployment domain logic, not catalog logic. Use the existing catalog EF database as the persistence adapter because account/workspace ownership and current persisted workspace resources already live there. Keep permission decisions in deployment core but back them with server-authoritative persistence. Keep API endpoints as authorization/contract adapters and keep console state in the existing deployments feature directory.

## Phase Plan

### Phase 1: Planning And Contracts

Outcome:

- Feature spec, implementation plan, research, data model, contracts, quickstart, and tasks exist and align with the clarification decisions.

Exit gate:

- Requirements checklist passes and tasks are dependency-ordered.

### Phase 2: Flexible Permissions Foundation

Outcome:

- Workspace permission grant model, bootstrap grants for workspace owners, permission resolution, and permission-gated deployment operation checks exist.

Exit gate:

- Tests prove setup, desired-state management, preview, deploy, rollback, runtime control, observability, and read permissions are independently grantable and enforced server-side.

### Phase 3: Durable Cockpit Foundation

Outcome:

- Workspace deployment entities, observability metadata, drift report metadata, EF mappings, migrations, store, and cockpit projection replace the in-memory seeded store for read paths.

Exit gate:

- Persistence and API tests prove workspace members can read only their workspace deployment cockpit, raw secrets are absent, observability/drift metadata is served from persisted records, and the normal cockpit dataset meets the bounded-query/under-3-second target.

### Phase 4: Engine Registration

Outcome:

- Authorized users can create/update workflow applications, environments, and engine registrations with credential references and capabilities.

Exit gate:

- API and console tests cover creation, permission denial, entitlement denial, refresh, and credential redaction.

### Phase 5: Structured Desired State And Promotion Preview

Outcome:

- Users can create structured desired-state revisions and compare them across environments with categorized diffs plus validation blockers/warnings. Manifest/artifact import/export remains deferred.

Exit gate:

- Tests prove preview is read-only, validation blocks unsafe deployment, structured desired-state records produce deterministic diffs, and console disables deploy actions when blockers exist.

### Phase 6: Durable Deployment Runs, Worker, Confirmation, And Rollback

Outcome:

- Authorized users can explicitly confirm and enqueue deployment/rollback runs; an in-process worker processes queued work, records status/history, and handles recovery after restart by processing queued runs normally and marking stale claimed runs `RecoveryRequired` without automatic duplicate apply.

Exit gate:

- Run/history tests cover actor, target, revision, validation outcome, confirmation same-user/single-use/expiration/replay rules, queued/running/succeeded/failed/`RecoveryRequired` states, active-run conflict, worker recovery, and rollback metadata.

### Phase 7: Capability-Gated Runtime Controls

Outcome:

- Engine operations require explicit confirmation and are shown/executed only when supported by engine or hosting capabilities and allowed by workspace permissions.

Exit gate:

- Direct API and console tests prove unsupported, unconfirmed, and unauthorized controls are rejected.

### Phase 8: Console Completion And E2E

Outcome:

- Deployment setup, cockpit, preview, deploy, rollback, history, drift/observability metadata, and controls are usable through the console without seeded demo data.

Exit gate:

- Console unit tests, typecheck, focused API/core tests, and one E2E smoke test pass.

## Complexity Tracking

No constitution violations are expected. The deliberate cross-subsystem dependency is the EF adapter from catalog persistence to deployment core contracts, matching the existing modular monolith storage boundary.
