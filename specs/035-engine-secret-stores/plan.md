# Implementation Plan: Engine Credential Secret Stores

**Branch**: `035-engine-secret-stores` | **Date**: 2026-06-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/035-engine-secret-stores/spec.md`

## Summary

Promote the existing deployment secret-store metadata into a focused engine credential store model. The implementation keeps secret stores workspace-scoped and engine-credential-only, adds explicit store types, supports local encrypted credential material without exposing submitted values, lets engine registration defer credentials, and updates the console setup flow so users can create stores/references or defer credentials instead of hitting a dead-end picker.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permissions, EF Core catalog persistence, `Elsa.Platform.Deployment.Core` workspace services, React Router, TanStack Query, Vitest, xUnit, and FluentAssertions.

**Storage**: Existing catalog relational database through `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Existing deployment secret-store and credential-reference tables are extended in place. Local encrypted credential values are stored only as protected ciphertext metadata, while external providers store only safe locators.

**Testing**: Focused xUnit/FluentAssertions tests for deployment API and persistence; Vitest tests for console deployment setup and secret-store flows; `npm run typecheck`; `git diff --check`; browser verification where console flow behavior cannot be proven by component tests alone.

**Target Platform**: Elsa Platform API and hosted admin console.

**Project Type**: Modular monolith web/control-plane service with hosted React console.

**Performance Goals**: Workspace credential store/reference lists remain metadata-only and load with deployment setup data without external provider calls. Engine setup should remain responsive when a workspace has at least 50 credential references.

**Constraints**: Engine credential stores are not runtime secret stores. Deployment artifacts may include runtime secret references, but those are outside this model. Raw secret values, provider tokens, decrypted credentials, and unsafe provider diagnostics must not appear in UI, API responses, logs, histories, audit records, artifacts, command records, or desired-state records. Existing legacy engine credential strings remain readable during transition.

**Scale/Scope**: Applies to deployment setup APIs, workspace deployment persistence, deployment console setup screens, and tests. First-class provider browsing and deep provider verification are out of scope, except for explicit verification state metadata and local encrypted value handling.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. The feature manages platform-to-engine credential metadata and command eligibility; it does not reconcile runtime workflow instance state or runtime secret state.
- **Bounded Subsystems**: PASS. Deployment core owns workspace contracts; persistence owns storage; API exposes workspace routes; console consumes those contracts. No deployment-core dependency on API or persistence internals is introduced.
- **Contract Stability**: PASS. Changes are additive to existing secret-store/reference and engine contracts. Existing free-text provider/reference values remain readable while new credential reference assignments improve the model.
- **Safety By Design**: PASS. The plan distinguishes engine credentials from runtime secrets and prohibits raw secret exposure. Local encrypted storage stores protected credential material only.
- **Incremental Verifiability**: PASS. Store type support, local encrypted credential submission, deferred engine credentials, assignment updates, and console wizard behavior can be tested independently.

Post-design re-check: PASS. The design artifacts keep the feature additive, workspace-scoped, engine-credential-only, and safe-metadata-first.

## Project Structure

### Documentation (this feature)

```text
specs/035-engine-secret-stores/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── engine-credential-api.md
│   └── console-engine-credential-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Platform.Deployment.Core/
│   └── Workspace/
├── Elsa.Platform.Api/
│   └── Workspace/
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
│   ├── Models/
│   └── DeploymentWorkspaceStore.cs
├── Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/
│   └── Migrations/
├── Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/
│   └── Migrations/
└── Elsa.Platform.Console/
    └── src/features/deployments/

tests/
├── Elsa.Platform.Api.Tests/
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
└── Elsa.Platform.Console/
```

**Structure Decision**: Extend the existing deployment workspace model, EF store, API route group, and console deployment feature in place. Avoid a standalone secret manager subsystem because this feature is explicitly scoped to engine credentials for deployment setup.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
