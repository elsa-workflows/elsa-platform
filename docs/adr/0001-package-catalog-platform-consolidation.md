# ADR-0001: Consolidate Package Catalog Into Elsa Platform

Status: accepted

Date: 2026-05-19

## Context

`elsa-package-catalog` contains package manifest contracts, manifest generation, package source synchronization, package approval, compatibility validation, catalog APIs, admin UI, persistence, migrations, Azure deployment assets, tests, and Spec Kit specs.

The Elsa Deployment Platform roadmap needs package descriptor validation, package compatibility metadata, approval/trust signals, and package manifest contracts. Keeping these capabilities in a separate long-term repository would create avoidable drift between deployment, catalog, Runtime Builder, and package publishing workflows.

## Decision

Move Package Catalog into `elsa-platform` as a first-class sibling subsystem to Deployment.

Target package family:

```text
src/
  Elsa.Platform.Deployment.*
  Elsa.Platform.PackageCatalog.*
  Elsa.Platform.PackageManifests
  Elsa.Platform.PackageManifest.Generator
  Elsa.Platform.PackageManifest.Generator.Core
  Elsa.Platform.PackageManifest.Generator.MSBuild
```

Package Catalog must not be nested under Deployment. Deployment may consume catalog abstractions or client contracts, but not catalog API, UI, EF persistence, migrations, AppHost, or source-provider internals.

## Consequences

Positive:

- One platform repository owns deployment and package governance.
- Package descriptor validation can share catalog compatibility and approval semantics.
- Package manifests remain a shared platform contract.
- The old catalog repository can be deprecated once the platform subsystem is usable.

Tradeoffs:

- The platform repository becomes larger earlier.
- Migration must handle Spec Kit, solution, project, namespace, EF migration, UI, and deployment asset conflicts.
- Package identity compatibility must be reviewed before publishing renamed packages.

## Implementation Notes

- Preserve catalog behavior before architectural cleanup.
- Attempt a history-preserving import.
- Keep platform Spec Kit infrastructure active.
- Use `specs/001-platform-package-catalog-consolidation/tasks.md` as the progress tracker.
