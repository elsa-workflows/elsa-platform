# Research: Platform Package Catalog Consolidation

## Decision: Consolidate Package Catalog Into Elsa Platform

**Rationale**: Package Catalog is platform control-plane infrastructure. It manages package metadata, manifests, approval, compatibility, sources, and sync history, all of which support Deployment, Runtime Builder, and package governance.

**Alternatives considered**:

- Keep `elsa-package-catalog` separate indefinitely. Rejected because package descriptor validation and governance would drift from deployment planning.
- Move Package Catalog under Deployment. Rejected because catalog also serves Runtime Builder, package publishers, and administrators.

## Decision: Use Sibling Subsystems

**Rationale**: Deployment and Package Catalog should evolve independently and integrate through contracts.

**Alternatives considered**:

- Deployment references catalog internals. Rejected because it would couple reconciliation to catalog persistence/API/UI.
- Catalog depends on deployment engine. Rejected because package discovery and approval do not require deployment.

## Decision: Preserve Behavior Before Improving Architecture

**Rationale**: The catalog repo already contains API, UI, EF persistence, migrations, NuGet sync, manifest contracts, generator, specs, and tests. A behavior-preserving import reduces migration risk.

**Alternatives considered**:

- Rewrite into the ideal package structure immediately. Rejected because it would mix migration risk with architectural changes.

## Decision: Attempt History-Preserving Import

**Rationale**: Existing catalog work has useful history and specs. Preserving history helps future archaeology.

**Fallback**: If history-preserving import is blocked by tooling or conflicts, import a snapshot and document the old repository commit SHA and rationale.

## Decision: Rename Toward `Elsa.Platform.*`

**Rationale**: The ideal end state should make platform ownership explicit:

- `Elsa.Platform.PackageCatalog.*`
- `Elsa.Platform.PackageManifests`
- `Elsa.Platform.PackageManifest.Generator*`

**Compatibility note**: If old package IDs are already externally consumed, provide compatibility aliases or deprecation packages for one release cycle.

## Decision: Keep Manifest Contracts Dependency-Light

**Rationale**: Package manifests are shared by generator, catalog ingestion, runtime validation, builder clients, and deployment validation. They must remain wire contracts, not catalog domain objects.

## Decision: Source Providers Are Adapters

**Rationale**: NuGet is the first source type, but catalog core should not assume NuGet. Moving NuGet sync into `Elsa.Platform.PackageCatalog.Sources.NuGet` makes future source types possible without catalog core churn.

## Decision: Deployment Integration Uses Abstractions Or Client Contracts

**Rationale**: Deployment Phase 1 needs package descriptor validation, not package installation. It should query package validity, approval, trust, and compatibility through a catalog-facing abstraction or API client.

## Open Follow-Up Decisions

- Whether to publish renamed package IDs immediately or retain old package IDs for compatibility.
- Whether old API routes remain under `/api` or gain platform/catalog route prefixes.
- Whether EF migration assembly names are reset, preserved, or bridged through transitional migration assemblies.
- Whether catalog specs are imported as historical specs or rewritten into new platform specs.

## Current Catalog Baseline

Repository: `https://github.com/elsa-workflows/elsa-package-catalog`

Inspected HEAD before PR #36 review: `7817031f9ff8049fe45d9a2915c39af2b35aaf40`

Updated inspected HEAD after merged PR #36: `e321965a09cdcf63bb6ad3144badd9a203c10da8`

Merged PR reviewed: [elsa-workflows/elsa-package-catalog#36](https://github.com/elsa-workflows/elsa-package-catalog/pull/36), "Add platform Runtime Builder backend foundations", merged 2026-05-19.

Current source projects:

- `Elsa.Platform.PackageCatalog.Api`
- `Elsa.Platform.PackageCatalog.AppHost`
- `Elsa.Platform.PackageCatalog.Core`
- `Elsa.Platform.PackageCatalog.Sources.NuGet`
- `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore`
- `Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations`
- `Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations`
- `Elsa.Platform.PackageCatalog.ServiceDefaults`
- `Elsa.Platform.PackageManifest.Generator`
- `Elsa.Platform.PackageManifest.Generator.Core`
- `Elsa.Platform.PackageManifest.Generator.MSBuild`
- `Elsa.Platform.PackageManifests`

Current test projects and UI test packages:

- `Elsa.Platform.PackageCatalog.Api.Tests`
- `Elsa.Platform.PackageCatalog.Core.Tests`
- `Elsa.Platform.PackageCatalog.Sources.NuGet.Tests`
- `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests`
- `Elsa.Platform.PackageCatalog.Testing`
- `Elsa.Platform.PackageManifest.Generator.Core.Tests`
- `Elsa.Platform.PackageManifest.Generator.IntegrationTests`
- `Elsa.Platform.PackageManifest.Generator.MSBuild.Tests`
- `Elsa.Platform.PackageManifest.Generator.Testing`
- `Elsa.Platform.PackageManifests.Tests`
- `Elsa.Platform.AdminUi.E2E`

Current specs:

- `001-package-catalog`
- `002-package-manifest-generator`
- `003-admin-dashboard-ui`
- `003-generator-adoption-fixes`
- `004-admin-dashboard-auth`
- `005-delete-sync-runs`
- `006-package-details-page`
- `007-source-scoped-catalog`
- `008-account-custom-feeds`
- `009-server-bundle-generation`
- `010-runtime-image-metadata-api`
- `011-saved-runtime-configurations`
- `012-server-side-planning`
- `013-deployment-template-expansion`
- `014-byoc-deployment-targets`
- `015-managed-hosting-control-plane`
- `016-runtime-operations`

## PR #36 Impact Review

PR #36 adds backend Runtime Builder foundations:

- Server-side bundle generation.
- Runtime image metadata API.
- Saved runtime configurations and snapshots.
- Server-side builder planning.
- Deployment template selection for Docker Compose, Azure Container Apps, and Kubernetes/Helm.
- Deferred specs for BYOC deployment targets, managed hosting, and runtime operations.

Architectural decision:

- Treat these capabilities as `Elsa.Platform.RuntimeBuilder.*`, not as Package Catalog core.

Rationale:

- Runtime Builder consumes package catalog data, but owns builder intent, runtime image metadata, generated bundles, server-side planning, deployment templates, and saved runtime configurations.
- Deployment may later consume Runtime Builder artifacts or contracts, but live reconciliation remains in `Elsa.Platform.Deployment.*`.
- BYOC deployment targets, managed hosting, and runtime operations should be later platform/deployment phases, not part of the initial catalog import.

Migration implications:

- Import PR #36 specs `009` through `016`; do not let old `.specify/feature.json` override the platform active feature.
- Map `src/Elsa.Platform.PackageCatalog.Core/Builder/*` to `Elsa.Platform.RuntimeBuilder.Core`.
- Map `src/Elsa.Platform.PackageCatalog.Core/DeploymentTemplates/*` to `Elsa.Platform.RuntimeBuilder.DeploymentTemplates`.
- Map `src/Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/*` and persistence stores to Runtime Builder contracts/persistence unless implementation feedback shows they should remain catalog-owned.
- Keep deployment template generation distinct from deployment apply/reconciliation.

## Spec Kit Conflict Notes

Both repositories contain Spec Kit infrastructure under `.specify/`, `AGENTS.md`, and `specs/`.

Decision:

- Keep `elsa-platform` `.specify/` as the active Spec Kit installation.
- Import old catalog specs under an archive or subsystem path instead of overwriting platform Spec Kit state.
- Merge useful catalog constitution guidance into the platform constitution only when it applies to the whole platform.
- Keep the active feature pointer in `.specify/feature.json` focused on the current implementation effort.

## Package ID Compatibility Status

NuGet flat-container checks on 2026-05-19 returned 404 for:

- `Elsa.Platform.PackageManifests`
- `Elsa.Platform.PackageManifest.Generator`

Working assumption:

- These package IDs are not published on nuget.org yet, so renaming to `Elsa.Platform.PackageManifests` and `Elsa.Platform.PackageManifest.Generator` is likely safe.

Required verification before publishing:

- Re-check nuget.org.
- Check internal/private feeds if any are used.
- Search downstream repositories for package references.

## Old Repository Deprecation Status

Checked on 2026-05-19:

- `gh issue list --repo elsa-workflows/elsa-package-catalog --state open --limit 100` returned no open issues.
- `gh pr list --repo elsa-workflows/elsa-package-catalog --state open --limit 100` returned no open PRs.
- No issue migration was required because there was no open issue backlog at the time of deprecation.
- The old repository README was updated on `main` in commit `cf7411d` to mark active development as moved to `https://github.com/elsa-workflows/elsa-platform`.

Archive timing decision:

- The old repository is deprecated and ready to archive after maintainers confirm no private/internal consumers still expect direct pushes to `elsa-package-catalog`.
- Do not archive before the platform consolidation branch is merged and the deployment integration contracts in Phase 8 are stable enough for downstream issue references.
