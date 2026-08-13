# Implementation Plan: Valence Control Package Catalog Consolidation

**Branch**: `001-valence-control-package-catalog-consolidation` | **Date**: 2026-05-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-valence-control-package-catalog-consolidation/spec.md`

## Summary

Move the existing `elsa-package-catalog` work into `valence-control` as a first-class Valence Control Package Catalog subsystem. The migration is phased: initialize Spec Kit and planning artifacts, import the existing repository behavior with history if practical, normalize project names and package boundaries, improve architecture where the move exposes clear seams, integrate deployment-facing catalog contracts, then deprecate the old repository.

Merged PR `elsa-workflows/elsa-package-catalog#36` adds Runtime Builder backend foundations to the catalog repository. The consolidation plan now treats that work as a separate target platform subsystem named `ValenceControl.RuntimeBuilder.*`, because server-side builder planning, runtime image metadata, bundle generation, deployment templates, and saved runtime configurations are adjacent to Package Catalog but not catalog core responsibilities.

The target package family is:

```text
src/
  ValenceControl.Deployment.*
  ValenceControl.PackageCatalog.*
  ValenceControl.RuntimeBuilder.*
  ValenceControl.PackageManifests
  ValenceControl.PackageManifest.Generator
  ValenceControl.PackageManifest.Generator.Core
  ValenceControl.PackageManifest.Generator.MSBuild
```

## Technical Context

**Language/Version**: C# on .NET 10 based on the current catalog repository `global.json` and project files; TypeScript/React/Vite for the catalog console.

**Primary Dependencies**: ASP.NET Core, Entity Framework Core, NuGet.Protocol, JsonSchema.Net, Aspire AppHost, Vite/React, existing Spec Kit workflow.

**Storage**: SQLite and SQL Server EF Core providers for Package Catalog persistence.

**Testing**: `dotnet test` for .NET projects; Vitest and Playwright for console tests where applicable.

**Target platform**: Cross-platform .NET service and tooling; Azure App Service deployment remains a catalog hosting option.

**Project Type**: Multi-project platform repository with library packages, web API, console, app host, CLI/deployment packages, and test projects.

**Performance Goals**: Preserve existing catalog sync behavior; no new sync performance target in the migration phase.

**Constraints**: Preserve catalog safety rules, maintain dependency-light manifest contracts, avoid deployment-to-catalog-internals coupling, keep old repository deprecation reversible until platform catalog builds and tests pass.

**Scale/Scope**: Import and normalize the current catalog codebase, specs, tests, UI, persistence, Azure deployment docs, and package manifest generator into `valence-control`.

## Constitution Check

- **Control Plane First**: Pass. Package Catalog is control-plane metadata and compatibility infrastructure.
- **Bounded Subsystems**: Pass with required contracts. Package Catalog remains a sibling subsystem to Deployment.
- **Contract Stability**: Pass with a compatibility decision gate before package ID renames are published.
- **Safety By Design**: Pass. Package inspection safety is an explicit migration requirement.
- **Incremental Verifiability**: Pass. The plan uses behavior-preserving import before architecture cleanup and tracks progress in `tasks.md`.

## Project Structure

### Documentation (this feature)

```text
specs/001-valence-control-package-catalog-consolidation/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── dependency-boundaries.md
│   ├── migration-map.md
│   └── progress-tracking.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

Target repository shape after consolidation:

```text
src/
  ValenceControl.Deployment.Abstractions/
  ValenceControl.Deployment.Manifest/
  ValenceControl.Deployment.Artifacts/
  ValenceControl.Deployment.Engine/
  ValenceControl.Deployment.Cli/
  ValenceControl.Deployment.Api/

  ValenceControl.PackageCatalog.Abstractions/
  ValenceControl.PackageCatalog.Core/
  ValenceControl.Api/
  ValenceControl.Console/
  ValenceControl.PackageCatalog.Sources.NuGet/
  ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/
  ValenceControl.PackageCatalog.Persistence.SqliteMigrations/
  ValenceControl.PackageCatalog.Persistence.SqlServerMigrations/
  ValenceControl.PackageCatalog.AppHost/
  ValenceControl.PackageCatalog.ServiceDefaults/
  ValenceControl.RuntimeBuilder.Abstractions/
  ValenceControl.RuntimeBuilder.Core/
  ValenceControl.RuntimeBuilder.Api/
  ValenceControl.RuntimeBuilder.DeploymentTemplates/
  ValenceControl.RuntimeBuilder.Persistence.EntityFrameworkCore/

  ValenceControl.PackageManifests/
  ValenceControl.PackageManifest.Generator/
  ValenceControl.PackageManifest.Generator.Core/
  ValenceControl.PackageManifest.Generator.MSBuild/

tests/
  ValenceControl.PackageCatalog.*.Tests/
  ValenceControl.RuntimeBuilder.*.Tests/
  ValenceControl.PackageManifests.Tests/
  ValenceControl.PackageManifest.Generator.*.Tests/
```

**Structure Decision**: Use one platform repository with sibling subsystems. Package Catalog and Runtime Builder are not nested under Deployment. Deployment consumes catalog and builder capabilities only through abstractions, artifacts, or client contracts.

## Phase Plan

### Phase 0: Planning And Baseline

Outcome:

- Spec Kit is initialized in `valence-control`.
- Valence Control constitution exists.
- Migration spec, plan, tasks, contracts, and progress tracking exist.
- Current catalog repository inventory is documented.

Exit gate:

- `tasks.md` can drive implementation without relying on conversation context.

### Phase 1: History-Preserving Import

Outcome:

- Import `elsa-package-catalog` into `valence-control`, preserving history if practical.
- Keep original project names initially if that reduces migration risk.
- Build and test current behavior from the platform repo.

Exit gate:

- Imported catalog solution or solution filter restores, builds, and runs existing test suites.
- No architecture cleanup is mixed into the first import except path fixes required to build.

### Phase 2: Valence Control Naming And Package Boundaries

Outcome:

- Rename projects and namespaces toward `ValenceControl.PackageCatalog.*`, `ValenceControl.PackageManifests`, and `ValenceControl.PackageManifest.Generator*`.
- Extract `ValenceControl.PackageCatalog.Abstractions`.
- Move NuGet-specific source sync to `ValenceControl.PackageCatalog.Sources.NuGet`.
- Extract Runtime Builder backend areas toward `ValenceControl.RuntimeBuilder.*`.
- Keep manifest contracts dependency-light.

Exit gate:

- Project references enforce the target dependency direction.
- Tests pass after renaming.
- Package identity compatibility decision is recorded.

### Phase 3: Architecture Improvements

Outcome:

- Separate API, core, source providers, persistence, UI, and app host concerns cleanly.
- Add catalog-facing contracts for deployment package descriptor validation.
- Add Runtime Builder contracts for runtime intent, planning, bundle output, deployment templates, and saved runtime configurations.
- Preserve approval, validity, trust, compatibility, source visibility, and sync state as separate states.

Exit gate:

- Deployment packages can reference only catalog abstractions/client contracts.
- Runtime Builder packages can reference Package Catalog abstractions or clients but not catalog persistence.
- No deployment project references catalog API, UI, or persistence.

### Phase 4: Old Repository Deprecation

Outcome:

- Update old repository README with redirect.
- Migrate or link issues/specs.
- Decide whether to archive the old repository.

Exit gate:

- Valence Control repo is the clear source of truth.
- Old repository has no active untriaged work.

## Complexity Tracking

No constitution violations are expected. The migration is large, but the complexity is justified by preserving existing catalog behavior while moving to the platform target architecture.
