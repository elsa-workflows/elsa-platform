# Contract: Dependency Boundaries

## Allowed Project Dependency Direction

```text
ValenceControl.PackageManifest.Generator*
  -> ValenceControl.PackageManifests

ValenceControl.PackageCatalog.*
  -> ValenceControl.PackageCatalog.Abstractions
  -> ValenceControl.PackageManifests

ValenceControl.Api
  -> ValenceControl.PackageCatalog.Core
  -> ValenceControl.PackageCatalog.Persistence.*
  -> ValenceControl.PackageCatalog.Sources.*

ValenceControl.RuntimeBuilder.*
  -> ValenceControl.RuntimeBuilder.Abstractions
  -> ValenceControl.PackageCatalog.Abstractions OR ValenceControl.PackageCatalog.Client
  -> ValenceControl.PackageManifests

ValenceControl.Deployment.*
  -> ValenceControl.Deployment.Abstractions
  -> ValenceControl.PackageCatalog.Abstractions OR ValenceControl.PackageCatalog.Client
  -> ValenceControl.RuntimeBuilder.Abstractions OR ValenceControl.RuntimeBuilder.Artifacts
  -> ValenceControl.PackageManifests
```

## Forbidden References

- Deployment must not reference Valence Control API, Valence Control Console, EF persistence, migrations, AppHost, or NuGet source provider projects.
- Package Manifests must not reference Package Catalog, Deployment, Generator implementation, persistence, hosting, ASP.NET Core, EF Core, or NuGet.Protocol.
- Catalog Core must not reference Valence Control Console.
- Catalog Core should not reference concrete source providers unless the boundary is explicitly reviewed.
- Runtime Builder must not reference Package Catalog EF persistence, migrations, Valence Control Console, or source-provider internals.
- Deployment must not reference Runtime Builder API endpoint implementation or persistence internals.

## Required Safety Rule

Catalog ingestion may inspect NuGet package files, nuspec metadata, and manifest JSON. It must not load or execute arbitrary package assemblies.

## Subsystem Ownership Notes

Package Catalog owns:

- Package source configuration and sync state.
- Package version indexing and immutable manifest hash handling.
- Approval, rejection, visibility, suspicious-version, and validation state.
- Public discovery APIs and admin APIs.
- Source providers such as NuGet.
- Catalog persistence and migrations.

Package Catalog does not own:

- Deployment reconciliation.
- Workflow artifact apply behavior.
- Runtime package installation.
- Runtime Builder UI composition.
- Raw secret storage.

Valence Control Console owns:

- Shared admin shell and design system.
- Catalog, deployment, runtime builder, target, managed runtime, operations, and audit module composition.
- Thin REST and SignalR client integration for backend-owned contracts.

Deployment may consume:

- Package manifest contracts.
- Package lookup, approval, and compatibility contracts.
- A catalog API client if cross-process validation is preferred.

Deployment may not consume:

- Catalog EF stores.
- Valence Control Console components.
- Catalog API endpoint implementation types.
- Catalog source-provider internals.

Runtime Builder owns:

- Builder intent contracts.
- Runtime image metadata.
- Server-side planning.
- Bundle generation.
- Deployment template rendering.
- Saved runtime configurations.

Runtime Builder does not own:

- Package source sync and approval.
- Live deployment reconciliation.
- Managed runtime lifecycle operations before the managed-hosting phase.

## Phase 5 Boundary Inspection

Inspection date: 2026-05-19.

Command:

```bash
rg -n "<ProjectReference" src tests --glob '!**/bin/**' --glob '!**/obj/**'
```

Observed state after platform naming normalization:

- `ValenceControl.PackageCatalog.Abstractions` exists and currently owns compatibility validation request/result contracts only.
- `ValenceControl.PackageCatalog.Core` references `ValenceControl.PackageCatalog.Abstractions` and `ValenceControl.PackageManifests`.
- `ValenceControl.Api` references abstractions directly because endpoint code maps public API requests into compatibility contracts.
- `ValenceControl.PackageManifests` has no project references to catalog, deployment, persistence, hosting, API, or generator implementation projects.
- `ValenceControl.PackageCatalog.Sources.NuGet` still references catalog Core because source-provider ports and entities have not yet been split into a source-provider abstraction.
- Runtime Builder services remain in Package Catalog projects until Phase 6. This is a tracked temporary state, not the final boundary.
- No Deployment projects exist yet, so deployment-to-catalog boundary checks become enforceable in Phase 8.

## Phase 6 Boundary Inspection

Inspection date: 2026-05-19.

Observed state after Runtime Builder extraction:

- `ValenceControl.RuntimeBuilder.Core` references `ValenceControl.RuntimeBuilder.Abstractions`, `ValenceControl.RuntimeBuilder.DeploymentTemplates`, and `ValenceControl.PackageCatalog.Abstractions`.
- `ValenceControl.RuntimeBuilder.Core` has no project reference to Package Catalog Core, Valence Control API, Package Catalog EF persistence, migrations, source providers, or Console.
- Runtime Builder reads catalog package projections through `IPublicCatalogQueries` from Package Catalog abstractions.
- Runtime Builder checks selected package compatibility through `IPackageCompatibilityService` from Package Catalog abstractions.
- Runtime configuration models and the `IRuntimeConfigurationStore` seam live in Runtime Builder abstractions.
- The current EF implementation of runtime configuration storage still lives in `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore` because the imported service currently uses the catalog database context. This is a host adapter detail, not a Runtime Builder Core dependency.
- Runtime Builder HTTP endpoints are still hosted by `ValenceControl.Api` pending a later `ValenceControl.RuntimeBuilder.Api` packaging decision.
- BYOC deployment targets, managed hosting, runtime operations, and fleet concerns remain deferred.

## Phase 8 Deployment Integration Boundary

Inspection date: 2026-05-19.

Deployment-facing package requirement validation now starts at `ValenceControl.PackageCatalog.Abstractions.Deployment.IDeploymentPackageCatalog`.

Required boundary:

- Deployment may reference `ValenceControl.PackageCatalog.Abstractions`.
- Deployment may not reference `ValenceControl.Api`, `ValenceControl.Console`, `ValenceControl.PackageCatalog.Persistence.*`, `ValenceControl.PackageCatalog.Sources.*`, `ValenceControl.PackageCatalog.AppHost`, or catalog migration projects.
- Deployment may validate package requirements through catalog abstractions or a future transport-specific client adapter.
- Deployment should consume deployment-specific manifests and artifacts. Runtime Builder intent or generated bundles may become artifact-build inputs later, but must not bypass deployment validation, diff, dry-run, apply, or history.

Enforcement:

- `tests/ValenceControl.PackageCatalog.Abstractions.Tests/DeploymentBoundaryTests.cs` scans future `src/ValenceControl.Deployment.*` project references for forbidden catalog internals.
- `tests/ValenceControl.PackageCatalog.Abstractions.Tests/DeploymentPackageContractsTests.cs` verifies the package requirement validation result shape keeps manifest, approval, trust, suspicious, and compatibility states separate.
