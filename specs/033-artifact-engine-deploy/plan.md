# Implementation Plan: Artifact To Engine Deployment

**Branch**: `033-artifact-engine-deploy` | **Date**: 2026-06-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/033-artifact-engine-deploy/spec.md`

## Summary

Connect registered deployment artifacts, desired-state revisions, engine capability metadata, runtime command dispatch, lease-scoped downloads, and runtime apply reporting into one complete deploy-to-engine flow. The implementation adds a structured deployability service/API, upgrades artifact apply capability normalization to canonical `artifact.{artifactTypeId}.apply` IDs, queues one runtime command per approved revision and target engine with all artifact records, and gives the console a preflight deployability surface with actionable blockers before deployment is enabled.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence/runtime command services; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity and deployment permissions, EF Core catalog persistence, `ValenceControl.Deployment.Core`, `ValenceControl.Deployment.Artifacts`, runtime command APIs, React Router, TanStack Query, Vitest, Playwright where browser verification is needed, xUnit, and FluentAssertions.

**Storage**: Existing catalog relational database through `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore` with SQLite and SQL Server migrations when schema changes are required. Deployment command storage must continue to store safe metadata only: artifact identities, type/schema metadata, digests, per-artifact outcomes, lease metadata, and diagnostics. Raw payloads, workflow definitions, local paths, credentials, provider tokens, and secrets stay out of command/history records.

**Testing**: Focused xUnit/FluentAssertions coverage for deployability, command creation, lease-scoped download authorization, partial apply outcomes, and EF persistence; ASP.NET Core endpoint tests for console/runtime contracts; Vitest for console preflight and blocker rendering; Playwright only for browser-level deployment UX if component tests cannot cover the workflow; `git diff --check`.

**Target platform**: Valence Control API plus hosted admin console, with runtime-facing command/download contracts consumed by Elsa workflow engine integrations.

**Project Type**: Modular monolith web/control-plane service with hosted React console and runtime-facing HTTP contracts.

**Performance Goals**: Deployability evaluation for a revision with at least 10 artifact records across 10 candidate engines completes within 3 seconds in integration tests. Runtime command polling and lease validation remain bounded to the existing indexed command lookup shape.

**Constraints**: Valence Control remains the control plane and must not reconcile workflow execution state. Runtime apply semantics stay in runtime integrations. Deployment is blocked when engine capability metadata is missing or stale. Runtime downloads require the active command lease for the target command and engine. Console and history surfaces must avoid raw paths and unsafe diagnostics.

**Scale/Scope**: First concrete artifact type is `elsa.workflow-definition`, but the model must support multiple artifact records and future artifact types through registry defaults and artifact-declared compatibility hints.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. Valence Control performs deployability, command dispatch, authorization, audit, and history. Runtime integrations apply artifacts locally and report safe outcomes; no workflow instance, bookmark, queue, lock, or runtime execution state is reconciled by Valence Control.
- **Bounded Subsystems**: PASS. Deployment Core owns deployability and command payload semantics; Deployment Artifacts owns artifact type/default apply requirements; API exposes workspace and runtime contracts; EF persistence stores deployment metadata; console consumes projected contracts. No catalog persistence types leak into Deployment Core contracts.
- **Contract Stability**: PASS. New API and command fields are additive and documented. Canonical apply capability IDs are version-stable, while legacy short capability IDs are handled through explicit normalization/migration behavior.
- **Safety By Design**: PASS. Downloads are authorized platform actions, runtime downloads are lease scoped, and command/run records contain safe references, digests, and diagnostics only.
- **Incremental Verifiability**: PASS. Deployability, command serialization, lease download authorization, runtime finalization, persistence, and console blocker rendering can be tested independently.

Post-design re-check: PASS. The design artifacts keep the feature additive, safe-metadata-only, workspace-scoped, and independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/033-artifact-engine-deploy/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── deployability-api.md
│   ├── runtime-artifact-download-api.md
│   └── console-deployability-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ValenceControl.Deployment.Artifacts/
│   ├── ArtifactEnvelopeModels.cs
│   └── ArtifactTypeRegistry.cs
├── ValenceControl.Deployment.Core/
│   └── Workspace/
│       ├── DeploymentCommandModels.cs
│       ├── DeploymentCommandService.cs
│       ├── DeploymentRunService.cs
│       ├── DeploymentValidationService.cs
│       ├── IWorkspaceDeploymentCommandStore.cs
│       ├── WorkspaceArtifactModels.cs
│       └── WorkspaceArtifactService.cs
├── ValenceControl.Api/
│   └── Workspace/
│       ├── RuntimeCommandContracts.cs
│       ├── RuntimeCommandEndpoints.cs
│       ├── WorkspaceArtifactEndpoints.cs
│       └── WorkspaceDeploymentEndpoints.cs
├── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
│   ├── DeploymentWorkspaceStore.cs
│   └── Models/
└── ValenceControl.Console/
    └── src/features/deployments/
        ├── DeploymentsPage.tsx
        ├── deploymentApi.ts
        └── deploymentModels.ts

tests/
├── ValenceControl.Deployment.Core.Tests/
├── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
├── ValenceControl.Api.Tests/
└── ValenceControl.Console/
```

**Structure Decision**: Extend the existing Deployment Core, runtime command, workspace artifact, EF store, API, and console deployment feature paths in place. Avoid a new subsystem because this feature connects existing deployment artifacts, revisions, engines, commands, and runs rather than introducing a separate deployment domain.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
