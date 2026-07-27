# Implementation Plan: Deployment Foundation Contracts

**Branch**: `017-deployment-contracts` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/017-deployment-contracts/spec.md`

## Summary

Create the first deployable Phase 1 foundation for Valence Control by adding dependency-light deployment abstractions, tests, and documentation. This slice establishes the shared language for deployable resources, artifact identity, targets, plans, changes, diagnostics, results, history, and extension contracts. It deliberately stops before manifest parsing, artifact folder/ZIP IO, reconciliation execution, CLI commands, hosted API endpoints, and runtime-specific handlers.

## Technical Context

**Language/Version**: C# on .NET 10 using the repository-wide `Directory.Build.props` target.

**Primary Dependencies**: Base class library only for `ValenceControl.Deployment.Abstractions`; xUnit and its built-in assertions for tests.

**Storage**: N/A for this slice. History is represented by contracts only; no persistence provider is implemented.

**Testing**: `dotnet test` with focused tests under `tests/ValenceControl.Deployment.Abstractions.Tests/` plus full solution verification.

**Target platform**: Cross-platform .NET library contracts intended for CLI, API, engine, operator, and third-party extension packages.

**Project Type**: Multi-project .NET platform repository; this slice adds one class library and one test project.

**Performance Goals**: Contract construction and equality operations must be deterministic and allocation-light enough for tests and planning. No runtime throughput target applies until engine implementation.

**Constraints**: Keep contracts dependency-light; do not reference Package Catalog implementation, Runtime Builder implementation, hosting, persistence, migration, UI, or runtime-state packages. Do not model raw secrets or runtime execution state.

**Scale/Scope**: Foundation contracts for Phase 1 deployment packages; no production reconciliation behavior yet.

## Constitution Check

- **Control Plane First**: Pass. Contracts model deployable control-plane state only and exclude runtime workflow instances, bookmarks, execution state, logs, locks, queues, and transient state.
- **Bounded Subsystems**: Pass. Deployment abstractions are a sibling subsystem and do not depend on catalog persistence/API/UI or Runtime Builder implementation.
- **Contract Stability**: Pass. The slice creates v1alpha-ready contract names before public adoption and records deferred decisions.
- **Safety By Design**: Pass. Artifacts must not contain raw secrets, and this slice has no package execution or arbitrary assembly loading behavior.
- **Incremental Verifiability**: Pass. The slice has independent tests and a task checklist before later manifest/artifact/engine work.

## Project Structure

### Documentation (this feature)

```text
specs/017-deployment-contracts/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── deployment-abstractions.md
│   └── dependency-boundaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ValenceControl.Deployment.Abstractions/
    Artifacts/
    Diagnostics/
    History/
    Plans/
    Resources/
    Targets/

tests/
  ValenceControl.Deployment.Abstractions.Tests/
    ArtifactContractTests.cs
    DependencyBoundaryTests.cs
    DiagnosticContractTests.cs
    ExtensionContractTests.cs
    HistoryContractTests.cs
    PlanContractTests.cs
    ResourceIdentityTests.cs
```

**Structure Decision**: Add only `ValenceControl.Deployment.Abstractions` in this slice. The roadmap packages `Manifest`, `Artifacts`, `Engine`, `Cli`, and `Api` remain deferred so their designs can be shaped by the tested foundation contracts.

## Phase Plan

### Phase 1: Specification And Planning

Outcome:

- Spec Kit feature artifacts describe the scope and deferred work.
- Contract and boundary decisions are documented.

Exit gate:

- `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`, and `tasks.md` are aligned.
- `/speckit-analyze` reports no critical consistency issues.

### Phase 2: Project Skeleton

Outcome:

- `ValenceControl.Deployment.Abstractions` and test project exist in the solution.
- Namespace and folder structure match the roadmap.

Exit gate:

- Solution restores and the new empty projects build.

### Phase 3: Core Contract Model

Outcome:

- Resource identity, artifact identity, diagnostics, plan/change/result, target, and history models exist with validation and deterministic equality where appropriate.

Exit gate:

- Contract tests cover required identities, statuses, diagnostics, and result composition.

### Phase 4: Extension Contracts And Boundaries

Outcome:

- Minimal resource handler, target state reader, validator, artifact reader/writer, engine, target, and history store interfaces exist.
- Boundary tests prove no forbidden project/package references and no runtime-state vocabulary in public contracts.

Exit gate:

- Sample test implementations compile for each extension point.
- Focused tests and full solution tests pass.

## Deferred Work

- Manifest YAML/JSON parsing and schema validation.
- Folder and ZIP artifact readers/writers.
- Deployment engine planning, validation orchestration, diff, dry-run, apply, and history persistence.
- CLI commands.
- Hosted API endpoints.
- Workflow and variable runtime adapters.
- Package/feature/recipe descriptor validation implementation.
- OCI, signing, approvals, overlays, operators, Kubernetes CRDs, policy engines, and multi-tenant reconciliation.

## Complexity Tracking

No constitution violations are introduced. The added abstraction package is justified because future manifest, artifact, engine, CLI, API, operator, and third-party resource packages require shared contracts without depending on implementation packages.
