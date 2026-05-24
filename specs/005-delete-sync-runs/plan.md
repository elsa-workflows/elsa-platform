# Implementation Plan: Delete Sync Runs

**Branch**: `005-delete-sync-runs` | **Date**: 2026-05-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-delete-sync-runs/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add an administrator-only sync run cleanup capability that can delete a single terminal sync run or all terminal sync runs completed before an explicit UTC cutoff. The implementation stays in the existing ASP.NET Core admin API, `Elsa.Platform.PackageCatalog.Core` sync domain, EF Core persistence adapter, and console sync-runs feature. Cleanup preserves package sources, packages, package versions, manifests, validation results, approvals, and public catalog state while removing only sync run history and dependent item diagnostics.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence; TypeScript + React for the existing console.

**Primary Dependencies**: ASP.NET Core minimal APIs and authorization, Entity Framework Core, SQLite/SQL Server EF migrations, React Router, TanStack Query, TailwindCSS, shadcn/ui-style local components.

**Storage**: Existing relational catalog database. No new durable entity is required; existing `SyncRuns` and `SyncRunItems` are deleted with existing cascade semantics.

**Testing**: xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory integration tests, EF Core persistence tests, Vitest/Testing Library for console behavior where UI controls are added.

**Target Platform**: ASP.NET Core API container deployed to Azure App Service with the built React console served by the API host.

**Project Type**: Modular monolith web service with static console assets.

**Performance Goals**: Bulk cleanup can delete at least 1,000 eligible sync runs in one administrator request and return a count summary within 30 seconds in normal local/test database conditions.

**Constraints**: Cleanup must be explicit, administrator-only, UTC-based, idempotent, and must not delete non-terminal runs or any package catalog state. No background retention job, external archive, new storage service, or package-processing behavior change is in scope.

**Scale/Scope**: Internal admin surface for small operator teams; sync history list currently shows the latest 100 runs, while cleanup must handle accumulated history beyond that list.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The plan MUST answer these gates:

- **Manifest-first**: Does package metadata flow through explicit, versioned manifests rather than package code execution?
  - Not impacted. This feature only deletes sync history records.
- **No arbitrary code execution**: Does every package-processing path inspect only package files, nuspec metadata, and manifest JSON?
  - Not impacted. No package-processing path changes.
- **Stable contracts**: Are `Elsa.Platform.PackageManifests` changes dependency-light, versioned, and separate from persistence/runtime internals?
  - Not impacted. No manifest contract changes.
- **Schema evolution**: Are schema versioning, extension metadata, compatibility behavior, and breaking-change rules documented?
  - Not impacted.
- **Immutable versions**: Does package-version handling preserve existing manifests and flag suspicious content changes?
  - Preserved. Cleanup never deletes or mutates package versions or manifests.
- **Approval separation**: Are validation, approval, and listing modeled as separate concerns?
  - Preserved. Cleanup never deletes or mutates approvals, validation results, or listing state.
- **Explicit sources**: Are package sources configured explicitly with include/exclude scope?
  - Not impacted. Source configuration is untouched.
- **Safe public API**: Are public responses limited to valid, approved, listed versions?
  - Preserved. Public endpoints remain unchanged and catalog state is unaffected.
- **Debuggability**: Are sync runs, validation errors, indexing decisions, and suspicious changes persisted and inspectable?
  - Pass with scoped tradeoff. Recent and non-deleted sync runs remain inspectable; administrators explicitly delete obsolete history only after previewing scope. Cleanup activity is logged with counts.
- **Modular monolith**: Does the design avoid distributed infrastructure unless justified?
  - Pass. The design remains in the existing API/Core/Persistence/Console modules.
- **Runtime Builder readiness**: Do APIs and manifests support package discovery, feature selection, settings schemas, and compatibility checks?
  - Preserved. Runtime Builder-facing catalog data is unchanged.
- **Simplicity**: Are new abstractions, dependencies, and infrastructure justified by current requirements?
  - Pass. Adds a focused cleanup service and store methods over existing models; no new dependency or infrastructure.

## Project Structure

### Documentation (this feature)

```text
specs/005-delete-sync-runs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── admin-sync-run-cleanup.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Platform.PackageCatalog.Core/
│   └── Sync/
│       ├── PackageSyncService.cs
│       ├── SyncModels.cs
│       └── SyncRunCleanupService.cs
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
│   └── SyncRunStore.cs
├── Elsa.Platform.Api/
│   └── Admin/
│       └── Sync/
│           ├── AdminSyncContracts.cs
│           └── AdminSyncEndpoints.cs
└── Elsa.Platform.Console/
    └── src/
        └── features/
            └── sync-runs/

tests/
├── Elsa.Platform.PackageCatalog.Core.Tests/
│   └── SyncRunCleanupServiceTests.cs
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
│   └── SyncPersistenceTests.cs
├── Elsa.Platform.Api.Tests/
│   └── AdminSyncApiTests.cs
└── Elsa.Platform.Console/
    └── src/
        └── features/
            └── sync-runs/
                └── SyncRunsPage.test.tsx
```

**Structure Decision**: Keep cleanup rules in `Elsa.Platform.PackageCatalog.Core` because terminal-state protection and result counting are domain behavior. Implement deletion in the EF Core sync run store because it owns `SyncRuns` and `SyncRunItems`. Expose the capability through the existing admin sync endpoint group and add focused UI controls to the existing Sync Runs screen rather than creating a new admin destination.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
