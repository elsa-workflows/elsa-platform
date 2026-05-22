# Implementation Plan: Package Details Page

**Branch**: `006-package-details-page` | **Date**: 2026-05-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-package-details-page/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Replace the placeholder admin Package Details routes with a version-focused
operational inspection page. The implementation extends the existing
administrator package details projection to include source identity, canonical
package casing, per-version manifest metadata, feature and setting records,
dependency/conflict/compatibility JSON surfaces, validation findings, visibility
reasons, and stale-state signals. The React console will select the latest
indexed version by default, support direct links to versions and major sections,
provide in-page search/filtering for large sections, and keep trust-changing
actions scoped to package versions.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core/Persistence; TypeScript + React for the existing console.

**Primary Dependencies**: ASP.NET Core minimal APIs and authorization, Entity Framework Core, System.Text.Json, Elsa.Platform.PackageManifests JSON shape, React Router, TanStack Query, TailwindCSS, shadcn/ui-style local components, Vitest/Testing Library.

**Storage**: Existing relational catalog database. No new durable entity is required; the feature reads existing `Packages`, `PackageVersions`, `PackageSources`, validation result records, feature records, and feature setting records.

**Testing**: xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory integration tests, EF Core query/persistence tests where projection behavior needs coverage, Vitest/Testing Library for console behavior, and Playwright console E2E for route-level smoke coverage.

**Target Platform**: ASP.NET Core API container deployed to Azure App Service with the built React console served by the API host.

**Project Type**: Modular monolith web service with static console assets.

**Performance Goals**: Package details should show summary and version data within 2 seconds for packages with up to 100 indexed versions; in-page search/filtering should let administrators find validation, feature, setting, dependency, or manifest content within 30 seconds for large seeded records.

**Constraints**: Admin-only surface; no public API change; no manifest schema change; no direct manifest editing; no arbitrary package code execution; trust-changing actions remain package-version scoped; stale trust-changing actions are blocked until refresh.

**Scale/Scope**: Internal admin surface for small operator teams; must remain usable for at least 100 indexed versions, 200 features, 500 settings, and 1,000 validation findings on one package.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The plan MUST answer these gates:

- **Manifest-first**: Does package metadata flow through explicit, versioned manifests rather than package code execution?
  - Pass. Details display existing stored manifest JSON, feature projections, and validation records only.
- **No arbitrary code execution**: Does every package-processing path inspect only package files, nuspec metadata, and manifest JSON?
  - Pass. No package-processing or assembly-loading behavior is added.
- **Stable contracts**: Are `Elsa.Platform.PackageManifests` changes dependency-light, versioned, and separate from persistence/runtime internals?
  - Pass. No `Elsa.Platform.PackageManifests` contract changes are required; existing manifest JSON remains read-only data.
- **Schema evolution**: Are schema versioning, extension metadata, compatibility behavior, and breaking-change rules documented?
  - Pass. The page surfaces schema version and compatibility metadata already present on indexed versions; schema rules remain unchanged.
- **Immutable versions**: Does package-version handling preserve existing manifests and flag suspicious content changes?
  - Pass. The design displays stored hashes and suspicious-change evidence without replacing manifests.
- **Approval separation**: Are validation, approval, and listing modeled as separate concerns?
  - Pass. Visibility reasons explicitly separate approval, rejection, validation, listing, suspicious, source, manifest, and ingestion states.
- **Explicit sources**: Are package sources configured explicitly with include/exclude scope?
  - Pass. The page displays source identity and source state; it does not broaden discovery.
- **Safe public API**: Are public responses limited to valid, approved, listed versions?
  - Pass. The feature changes authenticated admin APIs/UI only; public endpoints remain unchanged.
- **Debuggability**: Are sync runs, validation errors, indexing decisions, and suspicious changes persisted and inspectable?
  - Pass. This feature improves admin inspectability for validation findings, visibility decisions, manifest hashes, and suspicious changes.
- **Modular monolith**: Does the design avoid distributed infrastructure unless justified?
  - Pass. Work stays inside existing API/Core/Persistence/Console modules.
- **Runtime Builder readiness**: Do APIs and manifests support package discovery, feature selection, settings schemas, and compatibility checks?
  - Pass. The page exposes features, settings, dependencies, conflicts, and compatibility metadata in ways aligned with future builder diagnostics.
- **Simplicity**: Are new abstractions, dependencies, and infrastructure justified by current requirements?
  - Pass. The design uses existing models, endpoints, UI feature folders, and local components; no new infrastructure or third-party dependency is planned.

## Project Structure

### Documentation (this feature)

```text
specs/006-package-details-page/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── admin-package-details.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Platform.PackageCatalog.Core/
│   ├── Packages/
│   │   ├── PackageModels.cs
│   │   └── PublicCatalogVisibilityPolicy.cs
│   ├── Manifests/
│   │   └── FeatureProjectionModels.cs
│   └── Persistence/
│       └── CatalogStoreContracts.cs
├── Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/
│   └── ApprovalStore.cs
├── Elsa.Platform.PackageCatalog.Api/
│   └── Admin/
│       └── Packages/
│           ├── AdminPackageContracts.cs
│           ├── AdminPackageEndpoints.cs
│           ├── AdminValidationEndpoints.cs
│           └── AdminApprovalEndpoints.cs
└── Elsa.Platform.Console/
    └── src/
        ├── app/
        │   ├── routes.tsx
        │   └── AppShell.tsx
        ├── features/
        │   └── packages/
        │       ├── PackageDetailsPage.tsx
        │       ├── PackagesPage.tsx
        │       ├── packageApi.ts
        │       └── packageModels.ts
        ├── components/
        │   ├── states/
        │   └── ui/
        └── lib/
            ├── api/
            ├── query/
            └── status/

tests/
├── Elsa.Platform.PackageCatalog.Api.Tests/
│   ├── AdminPackagesApiTests.cs
│   ├── AdminValidationApiTests.cs
│   └── AdminApprovalApiTests.cs
├── Elsa.Platform.PackageCatalog.Testing/
│   └── PublicCatalogSeedData.cs
└── Elsa.Platform.Console.E2E/
    └── package-details.spec.ts

src/
└── Elsa.Platform.Console/
    └── src/
        └── features/
            └── packages/
                ├── PackageDetailsPage.test.tsx
                └── packageModels.test.ts
```

**Structure Decision**: Keep data retrieval and mutation behavior in the existing admin package endpoint group and approval store. Keep package details UI inside the existing `features/packages` folder so the package list and details page share models, action helpers, status formatting, and query keys. Do not add a new backend storage model or a separate UI application.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
