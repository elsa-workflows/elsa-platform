# Implementation Plan: Weaver Copilot Agent

**Branch**: `034-weaver-copilot-agent` | **Date**: 2026-06-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/034-weaver-copilot-agent/spec.md`

## Summary

Turn the existing placeholder Weaver drawer into a backend-backed agentic workspace assistant using the GitHub Copilot SDK runtime. The implementation adds a bounded Weaver subsystem with configuration, session persistence, read-only workspace tools, plan drafting, approval-gated execution hooks, audit-friendly records, streaming console integration, and administrator documentation for GitHub Copilot-backed and BYOK provider modes.

## Technical Context

**Language/Version**: C# on .NET 10 for API/Core/Persistence and Copilot SDK integration; TypeScript/React for the hosted console.

**Primary Dependencies**: GitHub Copilot SDK for .NET, ASP.NET Core minimal APIs, existing workspace identity/authorization helpers, EF Core catalog persistence, existing deployment/core services, React Router, TanStack Query, Vitest, xUnit, FluentAssertions.

**Storage**: Existing catalog relational database through `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore`. Weaver stores safe session records, messages, tool-call summaries, immutable plans, approvals, execution summaries, and configuration metadata. Provider API keys and raw secrets are never stored.

**Testing**: xUnit and FluentAssertions for core/API/persistence behavior; Vitest/Testing Library for console drawer behavior; focused `dotnet test`, `npm test`, `npm run typecheck`, and `git diff --check`.

**Target platform**: Valence Control API/catalog host and React console. Hosted production deployments must be able to disable Weaver or run it with BYOK provider configuration.

**Project Type**: Modular monolith web service plus hosted console.

**Performance Goals**: Weaver read-only page explanations for seeded local data should start streaming within 2 seconds and complete within 10 seconds. Tool result payloads should be bounded and summarized before reaching the model. Long-running agent turns must be cancellable.

**Constraints**: Weaver must not reconcile data-plane workflow execution state. Generic shell, filesystem, and edit tools are unavailable in hosted production sessions. All platform tool calls must enforce current account/workspace/permission state and redact secrets before model or UI exposure.

**Scale/Scope**: Initial implementation targets workspace-scoped console sessions, deployment investigation, plan drafting, and configuration. Cross-organization operator mode and deep external MCP integrations are future work.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. Weaver inspects and plans control-plane operations and executes through existing control-plane APIs; it does not reconcile runtime workflow instance state.
- **Bounded Subsystems**: PASS. Weaver will be introduced as a bounded subsystem with abstractions for runtime, tools, persistence, and API contracts. Deployment and catalog data are accessed through existing services or read contracts.
- **Contract Stability**: PASS. New API contracts are additive under workspace routes. Plan/tool status values are explicit and versionable.
- **Safety By Design**: PASS. Raw secrets, provider keys, shell access, filesystem access, and arbitrary model-generated HTTP calls are excluded from the hosted runtime design.
- **Incremental Verifiability**: PASS. Read-only sessions, tool authorization/redaction, plan drafting, execution approval, configuration, and audit can be tested independently.

Post-design re-check: PASS. Design artifacts keep Weaver additive, bounded, authorization-first, and independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/034-weaver-copilot-agent/
├── prd.md
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── weaver-api.md
│   └── weaver-console-ux.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ValenceControl.Api/
│   └── Workspace/
│       ├── WorkspaceWeaverContracts.cs
│       └── WorkspaceWeaverEndpoints.cs
├── ValenceControl.Weaver.Core/
│   ├── Configuration/
│   ├── Runtime/
│   ├── Sessions/
│   ├── Tools/
│   └── Plans/
├── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
│   └── Models/
└── ValenceControl.Console/
    └── src/
        ├── app/
        └── features/weaver/

tests/
├── ValenceControl.Api.Tests/
├── ValenceControl.Weaver.Core.Tests/
├── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
└── ValenceControl.Console/
```

**Structure Decision**: Add `ValenceControl.Weaver.Core` for agent/session/tool logic so API endpoints remain thin and persistence stays behind store abstractions. Store EF entities in existing catalog persistence because workspace-owned operational metadata already lives there. Add console code under `features/weaver` and replace the placeholder drawer in `AppShell.tsx` with the feature component.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New core project | Weaver needs a bounded subsystem for runtime/tool/plan logic independent of API and persistence | Putting agent logic in `ValenceControl.Api` would couple transport, runtime orchestration, and domain policies |
