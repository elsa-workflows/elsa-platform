# Implementation Plan: Saved Runtime Configurations

**Branch**: `011-saved-runtime-configurations` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

## Summary

Add workspace-owned saved runtime configurations and explicit version snapshots using the existing account/workspace foundation. Saved records persist normalized builder intent and call the existing bundle generation service for regeneration.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence.

**Primary Dependencies**: ASP.NET Core minimal APIs, existing workspace identity adapter, EF Core, System.Text.Json, existing bundle generation service, xUnit, FluentAssertions.

**Storage**: Existing relational catalog database extended with runtime configuration and version snapshot tables.

**Testing**: API integration tests, EF Core mapping tests, core service tests.

**Target platform**: Existing ASP.NET Core modular monolith.

**Project Type**: Web service with workspace APIs and EF Core persistence.

**Performance Goals**: List/fetch operations remain bounded by workspace ID and do not scan unrelated workspaces.

**Constraints**: Anonymous builder remains local only. Versions are explicit immutable snapshots. No billing, complex RBAC, live deployment, or collaboration.

## Constitution Check

- **Manifest-first**: Pass; saved intent references manifest-derived package/features.
- **No arbitrary code execution**: Pass; no package execution.
- **Stable contracts**: Pass; no manifest contract changes.
- **Schema evolution**: Pass; persistence evolves separately.
- **Immutable versions**: Pass; snapshots preserve selected versions.
- **Approval separation**: Pass; saved intent does not imply package approval.
- **Explicit sources**: Pass; source IDs remain explicit.
- **Safe public API**: Pass; workspace APIs require membership.
- **Debuggability**: Pass; version snapshots are inspectable.
- **Modular monolith**: Pass.
- **Runtime Builder readiness**: Pass.
- **Simplicity**: Pass; explicit snapshots, no collaboration.

## Project Structure

```text
specs/011-saved-runtime-configurations/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-configurations-api.md
└── tasks.md
```

```text
src/
├── ValenceControl.PackageCatalog.Core/RuntimeConfigurations/
├── ValenceControl.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs
└── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/

tests/
├── ValenceControl.PackageCatalog.Core.Tests/
├── ValenceControl.Api.Tests/
└── ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
