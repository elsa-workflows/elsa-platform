# Implementation Plan: Account-Owned Custom Feeds

**Branch**: `008-account-custom-feeds` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-account-custom-feeds/spec.md`

## Summary

Add the account/workspace foundation required for paid custom package feeds. The first implementation slice introduces catalog-local accounts, external identity mapping, personal workspaces, workspace membership, entitlement snapshots, workspace-owned package sources, and authenticated workspace APIs. Public anonymous browsing remains limited to catalog-owned browseable sources, while authenticated workspace members can create entitled private sources and browse indexed workspace-owned sources alongside public sources.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence; existing TypeScript/React admin UI remains out of scope for this backend-first slice.

**Primary Dependencies**: ASP.NET Core minimal APIs and authorization, existing custom API-key admin authentication, lightweight workspace identity adapter for trusted request contexts, Entity Framework Core, SQLite/SQL Server EF migrations.

**Storage**: Existing relational catalog database with new account/workspace/identity/membership/entitlement tables and new ownership fields on `PackageSources`.

**Testing**: xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory integration tests, EF Core persistence tests, and focused core service tests.

**Target Platform**: ASP.NET Core Catalog API deployed as the existing modular monolith.

**Project Type**: Modular monolith web service with public APIs, workspace APIs, admin APIs, core domain services, and EF Core persistence adapters.

**Performance Goals**: Workspace source and package filters should add one bounded source-visibility predicate to existing catalog queries. Authenticated package browsing should avoid returning packages from unauthorized sources even when callers provide source IDs directly.

**Constraints**: Browser-supplied user IDs are not trusted. Private feed credentials and billing purchase flows are out of scope. Public APIs remain safe by default. Package identity remains `sourceId + packageId`.

**Scale/Scope**: First slice supports personal workspace provisioning, manual entitlement snapshots, custom unauthenticated NuGet source creation, source listing, and workspace-visible package browsing. Organization workspaces, billing-provider integration, and private feed credentials are deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Manifest-first**: Pass. Package metadata remains manifest-derived; account/source ownership only controls visibility and indexing scope.
- **No arbitrary code execution**: Pass. Custom feed indexing continues to use existing NuGet/package inspection paths and does not execute package assemblies.
- **Stable contracts**: Pass. No `Elsa.PackageManifests` changes are required.
- **Schema evolution**: Pass. Persistence and HTTP API contracts evolve separately from manifest schemas and are documented in this feature.
- **Immutable versions**: Pass. Package version immutability and suspicious-change checks remain unchanged.
- **Approval separation**: Pass. Workspace-owned packages still have package approval, version approval, listing, and validation state as separate concerns.
- **Explicit sources**: Pass. Custom feeds are stored as explicit configured sources before indexing.
- **Safe public API**: Pass. Anonymous public APIs continue to show only public browseable valid/approved/listed packages. Workspace visibility is added only to authenticated workspace endpoints.
- **Debuggability**: Pass. Existing sync run and source status diagnostics remain the operational surface for workspace-owned sources.
- **Modular monolith**: Pass. The implementation stays in existing API/Core/Persistence projects.
- **Runtime Builder readiness**: Pass. Workspace-visible source and package browsing keep source-qualified identity for future builder flows.
- **Simplicity**: Pass. The first slice uses a trusted-header identity adapter for development/test and leaves full OIDC/customer-service integration behind an interface.

## Project Structure

### Documentation (this feature)

```text
specs/008-account-custom-feeds/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── workspace-custom-feeds-api.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Catalog.Core/
│   ├── Accounts/
│   ├── Packages/
│   └── Sources/
├── Elsa.Catalog.Persistence.EntityFrameworkCore/
│   ├── AccountWorkspaceStore.cs
│   ├── PublicCatalogQueries.cs
│   ├── PublicSourceQueries.cs
│   └── Models/CatalogModelConfiguration.cs
├── Elsa.Catalog.Persistence.SqliteMigrations/
├── Elsa.Catalog.Persistence.SqlServerMigrations/
└── Elsa.Catalog.Api/
    ├── Authentication/
    ├── Admin/Workspaces/
    └── Workspace/

tests/
├── Elsa.Catalog.Core.Tests/
├── Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/
└── Elsa.Catalog.Api.Tests/
```

**Structure Decision**: Keep account/workspace domain concepts in `Elsa.Catalog.Core/Accounts`, EF Core persistence in the existing persistence project, and HTTP endpoints under new workspace/admin route groups in `Elsa.Catalog.Api`. Extend existing public catalog query services with an explicit optional workspace visibility context rather than creating a separate duplicate package-query stack.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
