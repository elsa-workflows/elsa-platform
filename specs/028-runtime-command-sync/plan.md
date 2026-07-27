# Implementation Plan: Runtime Command Sync

**Branch**: `028-runtime-command-sync` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/028-runtime-command-sync/spec.md`

## Summary

Add a durable deployment command contract and runtime sync API that separates deployment intent from delivery transport. Deployment runs remain the console-facing source of truth, while command records let external runtime integrations poll, claim, heartbeat, report progress, and complete or fail work without requiring inbound runtime endpoints. Webhooks are modeled as optional command-available notifications only; runtimes still fetch and claim authoritative commands from Valence Control.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console where command state appears in deployment history.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permissions, EF Core catalog persistence, `ValenceControl.Deployment.Core` deployment run services, `ValenceControl.Deployment.Artifacts` envelope metadata, xUnit and its built-in assertions, React Router, TanStack Query, and Vitest.

**Storage**: Existing catalog relational database through `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Command tables store command metadata, lease/attempt state, progress, safe diagnostics, artifact/revision references, and runtime result references only.

**Testing**: Focused `dotnet test` for Deployment.Core command lifecycle, PackageCatalog persistence, and API tests; console `vitest` for command history display if UI changes are needed; `git diff --check`.

**Target platform**: ASP.NET Core Valence Control API and runtime-facing workspace command endpoints.

**Project Type**: Modular monolith web service with EF-backed workspace persistence and runtime integration APIs.

**Performance Goals**: Runtime command polling for a normal workspace with 250 pending or historical commands should complete in under 3 seconds in the integration test environment.

**Constraints**: Runtime pull/sync is the default. Webhooks are notifications only. Direct push is explicit opt-in. Command payloads must not include raw secrets, workflow definitions, artifact payload content, or connection strings. Deployment history remains the console-facing authority.

**Scale/Scope**: First platform command contract for external runtime sync. Runtime package implementation, workflow artifact application, provider-specific webhook dispatch, and artifact-backed promotion are later slices.

## Constitution Check

- **Control Plane First**: Pass. Commands represent platform-owned deployment intent and metadata; runtime execution remains outside Valence Control.
- **Bounded Subsystems**: Pass. Deployment.Core owns command lifecycle models/services; EF persists records; API exposes runtime-facing contracts. Runtime appliers remain separate packages.
- **Contract Stability**: Pass. Runtime sync endpoints and command payloads are explicitly versioned through documented contracts before package consumers depend on them.
- **Safety By Design**: Pass. Commands carry references, digests, and safe diagnostics only; raw secrets and payload content are forbidden.
- **Incremental Verifiability**: Pass. Poll, claim, heartbeat, completion, stale recovery, duplicate delivery, API authorization, and persistence behavior are independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/028-runtime-command-sync/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── runtime-command-api.md
│   └── console-command-history-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ValenceControl.Deployment.Core/
    Workspace/
      DeploymentCommandModels.cs
      DeploymentCommandService.cs
      IWorkspaceDeploymentCommandStore.cs
      DeploymentRunService.cs
      DeploymentQueueWorker.cs

  ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
    DeploymentWorkspaceStore.cs
    Models/DeploymentWorkspaceEntities.cs
    Models/CatalogModelConfiguration.cs

  ValenceControl.Api/
    Workspace/
      RuntimeCommandContracts.cs
      RuntimeCommandEndpoints.cs
      WorkspaceDeploymentEndpoints.cs
    Program.cs

  ValenceControl.Console/
    src/features/deployments/
      deploymentModels.ts
      DeploymentsPage.tsx
      DeploymentsPage.test.tsx
```

```text
tests/
  ValenceControl.Deployment.Core.Tests/
    DeploymentCommandServiceTests.cs
    DeploymentQueueWorkerTests.cs

  ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    DeploymentCommandPersistenceTests.cs

  ValenceControl.Api.Tests/
    RuntimeCommandApiTests.cs
    WorkspaceDeploymentApiTests.cs
```

**Structure Decision**: Add command lifecycle models/services under Deployment.Core and extend the existing EF workspace deployment store rather than creating a parallel command subsystem. Runtime-facing API contracts live under workspace deployment ownership. Existing in-process queue behavior is preserved by making it an internal command consumer or bridge.

## Phase Plan

### Phase 1: Command Contract Foundation

Outcome:

- Command, lease, attempt, progress, result, diagnostics, and webhook notification contracts are modeled and documented.

Exit gate:

- Core tests prove valid command creation, idempotency key generation, safe diagnostic filtering, and unsupported action rejection.

### Phase 2: Persistence And Run Bridge

Outcome:

- Deployment runs create linked command records. Existing queued worker can claim/complete commands or bridge queued runs into command history.

Exit gate:

- Persistence tests prove poll ordering, claim exclusivity, lease updates, stale recovery, final-state idempotency, and run history projection.

### Phase 3: Runtime API

Outcome:

- Runtime integrations can poll, claim, heartbeat, progress, complete, fail, reject, and read command state through authorized APIs.

Exit gate:

- API tests prove workspace/engine authorization, no duplicate claim, safe response shaping, duplicate completion behavior, and cross-workspace denial.

### Phase 4: Webhook Trigger Semantics

Outcome:

- Webhook notification records/events are produced as command-available triggers without transferring authority.

Exit gate:

- Tests prove duplicate or lost webhook notifications do not affect command authority, and polling remains sufficient.

### Phase 5: Console History And Verification

Outcome:

- Deployment history exposes command lifecycle state, and quickstart results are recorded.

Exit gate:

- Focused backend/API tests, console tests if changed, and `git diff --check` pass.

## Complexity Tracking

No constitution violations are expected.
