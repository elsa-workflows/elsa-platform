# Implementation Plan: Source-Scoped Catalog And Account Roadmap

**Branch**: `007-source-scoped-catalog` | **Date**: 2026-05-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/007-source-scoped-catalog/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Make source/feed selection a first-class catalog concern. Public browsing and Runtime Builder catalog APIs will list and filter only catalog-indexed browseable sources, and package identity will become source-qualified by requiring `sourceId + packageId` for details, versions, and builder resolve selections. The same specification also establishes the roadmap for later account/workspace-owned custom feeds, external identity mapping, paid entitlements, and future central customer-service integration.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence; TypeScript + React for the existing console and future Lovable-facing integration contracts.

**Primary Dependencies**: ASP.NET Core minimal APIs and authorization, Entity Framework Core, SQLite/SQL Server EF migrations, React Router/TanStack Query for existing console, OpenID Connect/JWT validation when account integration is implemented.

**Storage**: Existing relational catalog database for sources/packages; later migrations add browseability, account/workspace ownership, external identity mappings, and entitlement snapshots.

**Testing**: xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory integration tests, EF Core persistence tests, and UI/integration tests where source selection behavior is implemented.

**Target Platform**: ASP.NET Core API container deployed to Azure App Service, with static console assets served by the API host and a separate Lovable-built public UX consuming public APIs.

**Project Type**: Modular monolith web service with public/catalog APIs, admin APIs, persistence adapters, and static console assets.

**Performance Goals**: Source-filtered public catalog responses should remain within current public catalog latency expectations and avoid loading packages from unselected sources. Selected-source cache keys should prevent cross-source result contamination.

**Constraints**: Public APIs must expose only valid, approved, listed, non-suspicious package versions. Feed URLs must be sanitized. Browser callers must not be trusted to supply arbitrary user IDs. Private feed credentials remain out of scope for the first implementation slice.

**Scale/Scope**: First slice targets public browseable source filtering and source-qualified package identity. Later slices add workspace-owned custom feeds, entitlement enforcement, and external customer-service reconciliation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The plan MUST answer these gates:

- **Manifest-first**: Pass. Package metadata remains manifest-derived; filtering and identity changes do not execute package code.
- **No arbitrary code execution**: Pass. Source filtering and account ownership do not change package inspection safety boundaries.
- **Stable contracts**: Pass. No `Elsa.Platform.PackageManifests` wire contract changes are required.
- **Schema evolution**: Pass. Public API contracts and persistence schema changes are documented separately from manifest schema evolution.
- **Immutable versions**: Pass. Source-qualified identity preserves existing version immutability and suspicious-change behavior.
- **Approval separation**: Pass. Public filtering still returns only approved/listed/valid versions and keeps approval independent from validity.
- **Explicit sources**: Pass. The feature reinforces explicit configured/indexed sources and rejects anonymous arbitrary feed browsing.
- **Safe public API**: Pass. Public responses are constrained by source accessibility plus existing visibility rules.
- **Debuggability**: Pass. Existing source, sync, validation, approval, and suspicious-change diagnostics remain inspectable; later workspace sources retain sync diagnostics.
- **Modular monolith**: Pass. The first slice stays inside existing API/Core/Persistence modules; later customer-service integration is modeled as an external dependency, not a distributed rewrite.
- **Runtime Builder readiness**: Pass. Builder catalog and resolve flows become source-qualified and deterministic.
- **Simplicity**: Pass. The first slice uses existing `SourceId` relationships before adding account/workspace abstractions only when paid custom feeds require them.

## Project Structure

### Documentation (this feature)

```text
specs/007-source-scoped-catalog/
├── plan.md
├── roadmap.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── public-source-scoped-api.md
│   └── account-workspace-roadmap.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Platform.PackageCatalog.Core/
│   ├── Packages/
│   │   ├── PublicCatalogQueryService.cs
│   │   └── PackageModels.cs
│   ├── Sources/
│   │   └── PublicSourceQueryService.cs
│   └── Accounts/                  # later account/workspace slice
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
│   ├── PublicCatalogQueries.cs
│   ├── PublicSourceQueries.cs
│   └── Models/CatalogModelConfiguration.cs
├── Elsa.Platform.PackageCatalog.Api/
│   └── Public/
│       ├── Sources/
│       ├── Packages/
│       └── Builder/
└── Elsa.Platform.Console/
    └── src/features/sources/       # admin/operator source flags if needed

tests/
├── Elsa.Platform.PackageCatalog.Core.Tests/
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
└── Elsa.Platform.PackageCatalog.Api.Tests/
```

**Structure Decision**: Keep public source filtering and source-qualified package queries in `Elsa.Platform.PackageCatalog.Core` and EF Core query adapters because visibility is domain/query behavior. Expose public browseable sources and source-qualified package routes under `Elsa.Platform.PackageCatalog.Api/Public`. Defer account/workspace source management to a later `Accounts` or `Workspaces` area once paid custom feeds are actively implemented.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
