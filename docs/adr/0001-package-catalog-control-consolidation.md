# ADR-0001: Consolidate Package Catalog Into Valence Control

Status: accepted

Date: 2026-05-19

## Context

`elsa-package-catalog` contains package manifest contracts, manifest generation, package source synchronization, package approval, compatibility validation, catalog APIs, console, persistence, migrations, Runtime Builder backend foundations, deployment template generation, Azure deployment assets, tests, and Spec Kit specs.

The Valence Control roadmap needs package descriptor validation, package
compatibility metadata, approval/trust signals, and package manifest contracts.
Keeping these capabilities in a separate long-term repository would create
avoidable drift between deployment, catalog, Runtime Builder, and package
publishing workflows.

## Decision

Move Package Catalog into `valence-control` as a first-class sibling subsystem to Deployment.

Target package family:

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

Package Catalog and Runtime Builder must not be nested under Deployment. Deployment may consume catalog abstractions, Runtime Builder contracts, or client contracts, but not catalog API, UI, EF persistence, migrations, AppHost, or source-provider internals.

Runtime Builder should be extracted as its own Valence Control subsystem when normalizing the imported catalog code. It owns builder intent, runtime image metadata, bundle generation, server-side planning, deployment template rendering, and saved runtime configurations.

## Consequences

Positive:

- One Valence Control repository owns deployment and package governance.
- Package descriptor validation can share catalog compatibility and approval semantics.
- Package manifests remain a shared Valence Control contract.
- Runtime Builder backend capabilities get a clear home instead of remaining mixed into catalog core.
- The old catalog repository can be deprecated once the Valence Control subsystem is usable.

Tradeoffs:

- The Valence Control repository becomes larger earlier.
- Migration must handle Spec Kit, solution, project, namespace, EF migration, UI, and deployment asset conflicts.
- Package identity compatibility must be reviewed before publishing renamed packages.

## Implementation Notes

- Preserve catalog behavior before architectural cleanup.
- Attempt a history-preserving import.
- Keep Valence Control's Spec Kit infrastructure active.
- Use `specs/001-valence-control-package-catalog-consolidation/tasks.md` as the progress tracker.
