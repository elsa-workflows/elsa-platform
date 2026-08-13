# Implementation Plan: Deployment Engine MVP

**Branch**: `020-deployment-engine` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/020-deployment-engine/spec.md`

## Summary

Implement the Phase 1 deployment engine package that proves the core deployment loop after manifest and artifact work: validate desired resources, produce deterministic dry-run plans, apply applyable changes through resource handlers, and record append-only history through an abstraction. The implementation stays in-process and host-agnostic, uses existing `ValenceControl.Deployment.Abstractions` concepts, closes only the proven contract gaps for artifact resource access and per-run execution context, consumes manifest/artifact outputs indirectly through artifact/resource abstractions, and defers CLI, API, persistence, approvals, signatures, GitOps, operators, and policy engines.

## Technical Context

**Language/Version**: C# on .NET 10.

**Primary Dependencies**: `ValenceControl.Deployment.Abstractions`, .NET base class libraries, xUnit and its built-in assertions for tests.

**Storage**: In-memory deployment history store only for Phase 1; durable persistence is deferred.

**Testing**: `dotnet test`, with focused tests under `tests/ValenceControl.Deployment.Engine.Tests`.

**Target platform**: Cross-platform .NET library package.

**Project Type**: Library package with companion unit/contract tests.

**Performance Goals**: Deterministic planning for at least 100 desired resources in memory without external services; no persistent or distributed performance target in this slice.

**Constraints**: No CLI, HTTP API, hosting, persistence provider, Kubernetes, OCI, signing, policy, or runtime-state dependencies. Dry-run must not mutate resource state or history. Apply is not transactional across resource types.

**Scale/Scope**: Phase 1 engine contract and in-memory tests, with extension points for resource handlers and history. Product-specific workflow/recipe/package/feature/variable handlers may follow after the core engine proves the loop.

## Constitution Check

- **Control Plane First**: Pass. The engine operates only on deployable control-plane resources and explicitly excludes workflow runtime state.
- **Bounded Subsystems**: Pass. `ValenceControl.Deployment.Engine` depends on deployment abstractions and not on catalog, API, CLI, hosting, or persistence internals.
- **Contract Stability**: Pass. The slice uses existing abstraction concepts first and changes public APIs only where analysis found implementation-blocking gaps: artifact resource enumeration and execution context.
- **Safety By Design**: Pass. The engine consumes validated desired state and does not package or resolve raw secrets.
- **Incremental Verifiability**: Pass. Validation, dry-run, apply, history, and boundary behavior are independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/020-deployment-engine/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── engine-contract.md
│   └── dependency-boundaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ValenceControl.Deployment.Engine/
    DeploymentEngine.cs
    DeploymentEngineOptions.cs
    DeploymentEngineDiagnosticCodes.cs
    InMemoryDeploymentHistoryStore.cs
    ResourceHandlerRegistry.cs

tests/
  ValenceControl.Deployment.Engine.Tests/
    DeploymentEngineValidationTests.cs
    DeploymentEnginePlanningTests.cs
    DeploymentEngineApplyTests.cs
    DeploymentEngineHistoryTests.cs
    DeploymentEngineBoundaryTests.cs
    DeploymentEngineTestFixtures.cs
```

**Structure Decision**: Add a new `ValenceControl.Deployment.Engine` package as a sibling to `Abstractions`, `Manifest`, and `Artifacts`. The engine consumes abstraction contracts only; CLI/API/persistence adapters are separate future packages.

**Contract Gap Decision**: Update `ValenceControl.Deployment.Abstractions` before engine implementation to add `IArtifactReader.ReadResourcesAsync(...)` and `DeploymentExecutionContext`. The engine must not reference `ValenceControl.Deployment.Manifest` or `ValenceControl.Deployment.Artifacts` concrete packages to compensate for missing abstraction shape.

## Phase 0 Research

See [research.md](./research.md).

## Phase 1 Design

See [data-model.md](./data-model.md), [contracts/engine-contract.md](./contracts/engine-contract.md), [contracts/dependency-boundaries.md](./contracts/dependency-boundaries.md), and [quickstart.md](./quickstart.md).

## Complexity Tracking

No constitution violations are expected. The package is additive and bounded.
