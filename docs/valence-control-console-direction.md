# Valence Control Console Direction

The console is a unified React console served from `/admin`. It is a thin web
app over platform REST APIs and SignalR streams where backend features expose
live operational updates.

## Product Shape

Use Option A, Valence Control Operations Console, as the primary shell and landing
experience. Option B and Option C are submodules:

- Environment Workbench: deployment environment detail module for desired state,
  last artifact, runtime health, diagnostics, and operations.
- Artifact Provenance: artifact detail module for manifest, payloads,
  checksums, validation, plans, dry-runs, apply runs, and history.

## Module Model

Initial active module:

- Package Catalog: sources, packages, package versions, approvals, validation,
  manifests, and sync runs.

Reserved modules:

- Deployment: manifests, immutable artifacts, validation, diff, dry-run, apply,
  and history.
- Runtime Builder: runtime images, saved configurations, planner, and generated
  bundles.
- Targets: BYOC target registration and connectivity state.
- Managed Runtimes: provisioned runtime environments and lifecycle actions.
- Runtime Operations: health, logs, backups, restores, upgrades, rollback, and
  incident visibility.
- Audit: cross-module actor, artifact, package, deployment, and operations
  history.

Reserved modules may be visible as roadmap affordances, but they must not imply
implemented backend mutations before contracts exist.

## Visual System

The console should share Valence Control's product identity but be denser and more
operational:

- Space Grotesk for product headings.
- Inter for interface text.
- JetBrains Mono for artifact IDs, hashes, code, and path-like data.
- Light mode should be the default to keep the console calm and less visually
  heavy. Dark mode remains a first-class switchable theme.
- Light mode uses cool near-white backgrounds, white surfaces, thin borders, and
  teal primary accents. Dark mode uses graphite/navy surfaces with the same teal
  primary accent and blue as a secondary accent when needed.
- Tables, split panes, tabs, timelines, diagnostics drawers, and compact status
  rows are preferred over card-heavy dashboards.
- Avoid Azure Portal-style blades, heavy blue chrome, nested card stacks, and
  generic KPI dashboard decoration.
