# Contract: Dependency Boundaries

## Allowed Project Dependency Direction

```text
Elsa.Platform.PackageManifest.Generator*
  -> Elsa.Platform.PackageManifests

Elsa.Platform.PackageCatalog.*
  -> Elsa.Platform.PackageCatalog.Abstractions
  -> Elsa.Platform.PackageManifests

Elsa.Platform.PackageCatalog.Api
  -> Elsa.Platform.PackageCatalog.Core
  -> Elsa.Platform.PackageCatalog.Persistence.*
  -> Elsa.Platform.PackageCatalog.Sources.*

Elsa.Platform.RuntimeBuilder.*
  -> Elsa.Platform.RuntimeBuilder.Abstractions
  -> Elsa.Platform.PackageCatalog.Abstractions OR Elsa.Platform.PackageCatalog.Client
  -> Elsa.Platform.PackageManifests

Elsa.Platform.Deployment.*
  -> Elsa.Platform.Deployment.Abstractions
  -> Elsa.Platform.PackageCatalog.Abstractions OR Elsa.Platform.PackageCatalog.Client
  -> Elsa.Platform.RuntimeBuilder.Abstractions OR Elsa.Platform.RuntimeBuilder.Artifacts
  -> Elsa.Platform.PackageManifests
```

## Forbidden References

- Deployment must not reference Package Catalog API, Admin UI, EF persistence, migrations, AppHost, or NuGet source provider projects.
- Package Manifests must not reference Package Catalog, Deployment, Generator implementation, persistence, hosting, ASP.NET Core, EF Core, or NuGet.Protocol.
- Catalog Core must not reference Admin UI.
- Catalog Core should not reference concrete source providers unless the boundary is explicitly reviewed.
- Runtime Builder must not reference Package Catalog EF persistence, migrations, Admin UI, or source-provider internals.
- Deployment must not reference Runtime Builder API endpoint implementation or persistence internals.

## Required Safety Rule

Catalog ingestion may inspect NuGet package files, nuspec metadata, and manifest JSON. It must not load or execute arbitrary package assemblies.

## Subsystem Ownership Notes

Package Catalog owns:

- Package source configuration and sync state.
- Package version indexing and immutable manifest hash handling.
- Approval, rejection, visibility, suspicious-version, and validation state.
- Public discovery APIs and admin APIs.
- Admin UI for catalog operations.
- Source providers such as NuGet.
- Catalog persistence and migrations.

Package Catalog does not own:

- Deployment reconciliation.
- Workflow artifact apply behavior.
- Runtime package installation.
- Runtime Builder UI composition beyond catalog/admin surfaces.
- Raw secret storage.

Deployment may consume:

- Package manifest contracts.
- Package lookup, approval, and compatibility contracts.
- A catalog API client if cross-process validation is preferred.

Deployment may not consume:

- Catalog EF stores.
- Catalog admin UI components.
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
