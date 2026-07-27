# Implementation Plan: Deployment Setup Domain Flow

**Branch**: `033-deployment-setup-domain-flow` | **Date**: 2026-06-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/033-deployment-setup-domain-flow/spec.md`

## Summary

Separate deployment environment creation from workflow engine registration, then introduce workspace-owned secret-store and credential-reference metadata so engine credential selection is discoverable and safe. The implementation extends the existing deployment workspace contracts, EF Core catalog persistence, workspace API endpoints, and React console setup screens while preserving legacy engine credential strings for existing data.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permissions, EF Core catalog persistence, `ValenceControl.Deployment.Core` workspace services, React Router, TanStack Query, Vitest, xUnit and its built-in assertions.

**Storage**: Existing catalog relational database through `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. New deployment secret-store and credential-reference tables store safe metadata only.

**Testing**: Focused deployment API and persistence tests using xUnit's built-in assertions; Vitest tests for console deployment setup flows; `npm run typecheck`; `git diff --check`.

**Target platform**: Valence Control API and hosted console.

**Project Type**: Modular monolith web service plus hosted React console.

**Performance Goals**: Deployment cockpit load remains bounded to workspace-scoped setup data. Secret-store/reference option lists are metadata-only and should not require external provider calls.

**Constraints**: No raw secret values may be stored or exposed. Existing workflow engine credential provider/reference strings remain readable during transition. Deployment domain packages must not depend on API or catalog implementation internals.

**Scale/Scope**: Applies to deployment setup APIs, workspace deployment persistence, deployment console setup screens, and tests. Real provider browsing/verification is deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. The feature manages deployment setup metadata and does not reconcile runtime workflow state.
- **Bounded Subsystems**: PASS. Deployment core exposes contracts; persistence implements storage; API and console consume contracts. No deployment-core dependency on API or persistence internals is introduced.
- **Contract Stability**: PASS. Existing engine credential strings are preserved; new registry contracts are additive.
- **Safety By Design**: PASS. Secret values remain outside Valence Control; only provider/reference metadata is stored.
- **Incremental Verifiability**: PASS. Environment creation, engine registration, secret registry APIs, and UI flows are independently testable.

Post-design re-check: PASS. Design artifacts keep the change additive, metadata-only, and independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/033-deployment-setup-domain-flow/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── deployment-setup-domain-flow.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ValenceControl.Deployment.Core/
│   └── Workspace/
├── ValenceControl.Api/
│   └── Workspace/
├── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
│   ├── Models/
│   └── DeploymentWorkspaceStore.cs
├── ValenceControl.PackageCatalog.Persistence.SqliteMigrations/
│   └── Migrations/
├── ValenceControl.PackageCatalog.Persistence.SqlServerMigrations/
│   └── Migrations/
└── ValenceControl.Console/
    └── src/features/deployments/

tests/
├── ValenceControl.Api.Tests/
└── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
```

**Structure Decision**: Extend the existing deployment workspace model, store, API route group, and console deployment feature in place. Avoid a new subsystem because secret-store metadata is part of deployment setup, not a standalone secret manager.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
