# Elsa Platform Constitution

## Core Principles

### I. Control Plane First
Elsa Platform owns deployable control-plane capabilities: manifests, artifacts, catalog metadata, compatibility, governance, deployment planning, and reconciliation. It must not reconcile Elsa data-plane state such as workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.

### II. Bounded Subsystems
Platform subsystems must have explicit module boundaries. Deployment, Package Catalog, Package Manifests, and Package Manifest Generator are sibling subsystems unless a dependency is deliberately introduced through an abstraction or client contract. Deployment must not depend on catalog persistence or API internals.

### III. Contract Stability
Wire contracts, manifest schemas, package IDs, CLI output intended for automation, and public APIs must be versioned deliberately. Renames are allowed before public adoption, but compatibility shims or deprecation notes are required once packages or APIs have known consumers.

### IV. Safety By Design
Catalog and generator code must inspect package files, manifests, project metadata, and assemblies only through safe metadata paths. Catalog ingestion must never load or execute arbitrary package assemblies. Deployment artifacts must not contain raw secrets.

### V. Incremental Verifiability
Every phase must have independently testable outcomes, clear decision gates, and a resumable task checklist. Migration work must preserve behavior before improving architecture, and improvements must be separated from mechanical moves when practical.

## Engineering Standards

- Prefer existing Elsa and .NET conventions unless a spec records a reason to diverge.
- Keep shared contracts dependency-light.
- Keep persistence, hosting, UI, and external source integrations out of core domain packages.
- Use clean tests with shared setup, focused fixtures, and teardown through `IAsyncDisposable` where appropriate.
- Preserve strict package safety guarantees around NuGet inspection and manifest generation.
- Use Spec Kit artifacts for implementation planning, task tracking, and phase gates.

## Development Workflow

- Start each substantial implementation effort from a Spec Kit feature under `specs/`.
- Keep `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`, and `tasks.md` aligned.
- Use `tasks.md` as the implementation progress tracker.
- Prefer history-preserving migrations when moving code between repositories.
- Run the narrowest useful test suite after each migration step, then broaden before phase completion.
- Commit mechanical moves separately from behavior changes where practical.

## Governance

This constitution guides Spec Kit planning and implementation in `elsa-platform`. Changes to these principles require an update to this file and a note in the relevant spec or ADR explaining the reason and migration impact.

**Version**: 1.0.0 | **Ratified**: 2026-05-19 | **Last Amended**: 2026-05-19
