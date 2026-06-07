# Implementation Plan: Deployment Artifact Registry

**Branch**: `024-artifact-registry` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/024-artifact-registry/spec.md`

## Summary

Add a workspace-scoped deployment artifact registry that stores immutable artifact metadata, safe diagnostics, and payload references without storing artifact payloads in the catalog database. The slice wires authorized API routes and a real Artifacts console view to list, register, inspect, and refresh artifact metadata, reusing existing `Elsa.Platform.Deployment.Artifacts` contracts for layout, digest, metadata, and checksum concepts. It deliberately stops before upload storage, OCI, signing, GitOps, validation, dry-run, apply, and provider-specific artifact transport.

> **Forward compatibility note**: `specs/031-organization-tenancy` keeps artifact records workspace-owned but resolves workspace ownership through a root Organization tenant.

> **Upload PRD amendment**: The completed registry slice deliberately stopped before payload upload. The PRD now defines a follow-up upload ingestion slice where the console uploads ZIP artifacts to a configured artifact blob store, the backend derives digest/manifest/resource metadata server-side, and the catalog database remains metadata-only.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permissions, EF Core catalog persistence, `Elsa.Platform.Deployment.Artifacts`, React Router, TanStack Query, Vitest, xUnit, and FluentAssertions.

**Storage**: Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Artifact records store metadata, digests, resource summaries, diagnostics, and references only. Raw payload files, manifest JSON, workflow definitions, tokens, and secrets are not stored.

**Testing**: Focused `dotnet test` for Deployment.Core, PackageCatalog persistence, and API tests; console `vitest` and typecheck for Artifacts page and navigation; `git diff --check`.

**Target Platform**: ASP.NET Core Platform API and React console served from the platform host.

**Project Type**: Modular monolith web service with React console and EF-backed workspace persistence.

**Performance Goals**: Artifact list for a normal workspace with 250 registered artifacts should return in under 3 seconds in the integration test environment with bounded queries.

**Constraints**: Workspace remains the artifact resource isolation boundary, and `specs/031-organization-tenancy` makes Organization the customer tenant boundary above it. Deployment core contracts remain persistence- and hosting-free. The registry stores metadata only and never stores raw artifact payloads or raw secrets. Refresh inspection initially supports local/test filesystem references only and must fail closed for unsupported references.

**Scale/Scope**: First hosted artifact registry slice for many workspaces, many artifacts per workspace, and folder/ZIP artifact metadata produced by the existing artifact package.

## Constitution Check

- **Control Plane First**: Pass. The feature registers control-plane deployment artifacts and never reconciles runtime data-plane state.
- **Bounded Subsystems**: Pass. Deployment.Core owns workspace artifact models/services; the EF and API layers adapt them. Existing `Deployment.Artifacts` remains the artifact IO package.
- **Contract Stability**: Pass. New workspace API and console contracts are documented under `contracts/` before implementation. Artifact layout version continues to come from the artifact package contract.
- **Safety By Design**: Pass. Artifact payloads, manifest JSON, workflow definitions, credentials, tokens, and raw secrets are excluded from catalog records and customer responses.
- **Incremental Verifiability**: Pass. Registry persistence, API isolation, inspection refresh, and console UX are independently testable phases.

## Project Structure

### Documentation (this feature)

```text
specs/024-artifact-registry/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── artifact-registry-api.md
│   └── console-artifacts-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  Elsa.Platform.Deployment.Core/
    Workspace/
      WorkspaceArtifactModels.cs
      WorkspaceArtifactService.cs
      IWorkspaceArtifactStore.cs

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
      WorkspaceArtifactContracts.cs
      WorkspaceArtifactEndpoints.cs
    Program.cs

  Elsa.Platform.Console/
    src/
      app/
        AppShell.tsx
        routes.tsx
      features/artifacts/
        artifactApi.ts
        artifactModels.ts
        ArtifactsPage.tsx
        ArtifactsPage.test.tsx
```

```text
tests/
  Elsa.Platform.Deployment.Core.Tests/
    WorkspaceArtifactServiceTests.cs

  Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    DeploymentWorkspaceArtifactPersistenceTests.cs

  Elsa.Platform.Api.Tests/
    WorkspaceArtifactApiTests.cs

  Elsa.Platform.Console.E2E/
    deployments.spec.ts
```

**Structure Decision**: Keep workspace registry orchestration in `Elsa.Platform.Deployment.Core`, use the existing catalog EF database as the workspace persistence adapter, expose API routes under workspace deployment/artifact ownership, and implement the console view in a new `features/artifacts` area.

## Phase Plan

### Phase 1: Planning And Contracts

Outcome:

- Spec, plan, research, data model, API contract, console contract, quickstart, and tasks align with the deployment PRD and artifact package boundary.

Exit gate:

- Analysis finds no blocking inconsistencies.

### Phase 2: Registry Foundation

Outcome:

- Workspace artifact models, store contract, EF entity/mapping/migrations, and service validation exist.

Exit gate:

- Core and persistence tests prove artifact metadata can be registered, duplicate identities fail closed, raw payload fields are rejected, and workspace isolation holds.

### Phase 3: API Integration

Outcome:

- Authorized workspace routes support list, detail, register, and inspection refresh.

Exit gate:

- API tests prove permission checks, cross-workspace denial, duplicate handling, and safe response shaping.

### Phase 4: Console Artifacts View

Outcome:

- Artifacts navigation is enabled and routes to a live workspace view with empty/list/detail/register/refresh states.

Exit gate:

- Console tests prove live API wiring, permission-blocked registration, invalid diagnostics, and no raw payload display.

### Phase 5: Verification

Outcome:

- Quickstart results are recorded and docs reflect implementation limits.

Exit gate:

- Focused backend tests, console tests/typecheck, and `git diff --check` pass or blocked commands are explicitly documented.

## Complexity Tracking

No constitution violations are expected.
