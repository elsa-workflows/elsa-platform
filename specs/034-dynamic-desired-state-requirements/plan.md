# Implementation Plan: Dynamic Desired-State Requirements

**Branch**: `034-dynamic-desired-state-requirements` | **Date**: 2026-06-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/034-dynamic-desired-state-requirements/spec.md`

## Summary

Make desired-state revision creation derive additional record editors from environment tier capabilities. Introduce a backend requirement catalog/API for environment desired-state requirements, use it from the console new revision page, hide observability on Dev/Test by default, show it as required for observability-required tiers, and support contextual validation links that pre-open a supported editor.

## Technical Context

**Language/Version**: C# on .NET 10 for deployment core/API; TypeScript/React for the hosted console.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace authorization and deployment permission grants, `ValenceControl.Deployment.Core` tier capability services, EF-backed deployment store projections, React Router, TanStack Query, Vitest, xUnit, FluentAssertions.

**Storage**: No new persistence. Requirement metadata is computed from existing environment tier capabilities and stable platform requirement definitions.

**Testing**: Focused xUnit API tests for requirement metadata; Vitest coverage for new revision form visibility/submission; `npm run typecheck`; focused console tests; focused API tests; `git diff --check`.

**Target platform**: Valence Control API and hosted React console.

**Project Type**: Modular monolith web service plus hosted console.

**Performance Goals**: Requirement metadata must be computed from already available environment/tier data and add no heavy artifact or engine queries.

**Constraints**: Do not introduce new database tables. Do not expose secrets. Preserve existing revision creation and validation behavior. Keep backend validation authoritative.

**Scale/Scope**: Initial implementation covers observability desired-state requirements and a generic shape for future requirement kinds.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. Desired-state requirements are deployment control-plane metadata and do not reconcile runtime workflow state.
- **Bounded Subsystems**: PASS. Deployment core owns requirement definitions; API exposes them; console consumes the contract. No catalog or runtime subsystem coupling is introduced.
- **Contract Stability**: PASS. The API contract is additive and uses stable capability, record kind, and validation IDs.
- **Safety By Design**: PASS. Requirement metadata contains no raw secrets or credential values.
- **Incremental Verifiability**: PASS. Backend metadata and frontend visibility can be tested independently.

Post-design re-check: PASS. The design stays additive, metadata-only, dependency-light, and independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/034-dynamic-desired-state-requirements/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── desired-state-requirements-api.md
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
└── ValenceControl.Console/
    └── src/features/deployments/

tests/
├── ValenceControl.Api.Tests/
└── ValenceControl.Console/src/features/deployments/
```

**Structure Decision**: Extend the existing workspace deployment core/API/console flow in place. Requirement metadata belongs next to tier capability and deployment validation logic, not in a new subsystem.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
