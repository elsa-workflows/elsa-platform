# Implementation Plan: Artifact Envelope And Types

**Branch**: `026-artifact-envelope-and-types` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/026-artifact-envelope-and-types/spec.md`

## Summary

Upgrade the artifact registry from metadata-only records toward a shared typed artifact envelope. The envelope gives Studio, CLI, CI, manual registration, and future producers a single submission contract while keeping Elsa Control a control plane that stores only safe metadata, digests, diagnostics, compatibility hints, and payload references. The first built-in artifact type is `elsa.workflow-definition`; type-specific payload interpretation remains in producer/runtime integration packages, not in platform core.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity/authorization and deployment permissions, EF Core catalog persistence, `ElsaControl.Deployment.Artifacts`, `ElsaControl.Deployment.Core` workspace artifact services, React Router, TanStack Query, Vitest, xUnit and its built-in assertions.

**Storage**: Existing catalog relational database through `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore`, with SQLite and SQL Server migrations. Catalog records store envelope metadata, type IDs, producer metadata, safe metadata, compatibility hints, digests, diagnostics, and payload references only.

**Testing**: Focused `dotnet test` for Deployment.Core artifact envelope validation, PackageCatalog persistence, and API tests; console `vitest` and typecheck for artifact list/detail envelope fields; `git diff --check`.

**Target platform**: ASP.NET Core Elsa Control API and React console served from the platform host.

**Project Type**: Modular monolith web service with React console and EF-backed workspace persistence.

**Performance Goals**: Artifact list/detail APIs continue to load a normal workspace with 250 artifacts in under 3 seconds in the integration test environment.

**Constraints**: Workspace remains the artifact resource isolation boundary, and `specs/031-organization-tenancy` makes Organization the customer tenant boundary above it. Elsa Control core validates envelope shape, registered type IDs, digests, safe metadata, and references but does not interpret workflow payload internals. Catalog persistence never stores raw payloads, workflow definitions, manifest JSON, credentials, tokens, connection strings, or secret values.

**Scale/Scope**: Envelope upgrade for the existing hosted artifact registry. Studio submit UX, runtime command sync, workflow runtime application, OCI storage, signing, and object storage upload are later slices.

## Constitution Check

- **Control Plane First**: Pass. The feature models deployable artifacts and metadata for orchestration, not runtime execution.
- **Bounded Subsystems**: Pass. `ElsaControl.Deployment.Artifacts` owns dependency-light envelope and type contracts; Deployment.Core orchestrates workspace registry behavior; EF persists metadata; API and console expose workspace-safe views.
- **Contract Stability**: Pass. Envelope, artifact type, API, and console contracts are documented before implementation.
- **Safety By Design**: Pass. The envelope explicitly excludes raw payloads and secrets from catalog storage and API responses.
- **Incremental Verifiability**: Pass. Type validation, duplicate behavior, safe metadata filtering, persistence, API, and console display are independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/026-artifact-envelope-and-types/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── artifact-envelope-contract.md
│   └── console-artifact-envelope-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ElsaControl.Deployment.Artifacts/
      ArtifactEnvelopeModels.cs
      ArtifactTypeModels.cs
      ArtifactEnvelopeValidator.cs
      ArtifactTypeRegistry.cs

  ElsaControl.Deployment.Core/
    Workspace/
      WorkspaceArtifactModels.cs
      WorkspaceArtifactService.cs
      IWorkspaceArtifactStore.cs

  ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/
    DeploymentWorkspaceStore.cs
    Models/DeploymentWorkspaceEntities.cs
    Models/CatalogModelConfiguration.cs

  ElsaControl.Api/
    Workspace/
      WorkspaceArtifactContracts.cs
      WorkspaceArtifactEndpoints.cs

  ElsaControl.Console/
    src/features/artifacts/
      artifactModels.ts
      artifactApi.ts
      ArtifactsPage.tsx
      ArtifactsPage.test.tsx
```

```text
tests/
  ElsaControl.Deployment.Artifacts.Tests/
    ArtifactEnvelopeValidationTests.cs

  ElsaControl.Deployment.Core.Tests/
    WorkspaceArtifactServiceTests.cs

  ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
    DeploymentWorkspaceArtifactPersistenceTests.cs

  ElsaControl.Api.Tests/
    WorkspaceArtifactApiTests.cs
```

**Structure Decision**: Add dependency-light envelope/type contracts in `ElsaControl.Deployment.Artifacts` so registry, Studio producers, future deployment commands, and runtime integration packages can share a stable model without depending on workspace orchestration. Reuse the existing workspace artifact store and API instead of introducing a parallel registry.

## Phase Plan

### Phase 1: Contracts And Type Catalog

Outcome:

- Artifact envelope, artifact type, producer, payload reference, digest, safe metadata, compatibility hint, and diagnostic contracts are documented and modeled.

Exit gate:

- Core tests prove built-in type registration, unknown type rejection, digest validation, and safe metadata filtering.

### Phase 2: Registry Upgrade

Outcome:

- Workspace artifact records persist envelope fields and project legacy records with default type/producer metadata.

Exit gate:

- Persistence tests prove round-trip, duplicate identity behavior, legacy projection, workspace isolation, and no raw payload/secret persistence.

### Phase 3: API And Console Exposure

Outcome:

- Workspace artifact APIs and console list/detail views show type, producer, compatibility hints, and envelope submission status.

Exit gate:

- API and console tests prove safe response shape, permissions, cross-workspace denial, duplicate rejection, and list/detail performance.

### Phase 4: Verification

Outcome:

- Quickstart results are recorded and docs reflect scope boundaries.

Exit gate:

- Focused backend tests, console tests/typecheck, and `git diff --check` pass.

## Complexity Tracking

No constitution violations are expected.
