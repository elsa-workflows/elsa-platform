# Contract: Migration Map

## Current To Target Project Mapping

| Current project | Target project |
| --- | --- |
| `ValenceControl.Api` | `ValenceControl.Api` |
| `ValenceControl.PackageCatalog.AppHost` | `ValenceControl.PackageCatalog.AppHost` |
| `ValenceControl.PackageCatalog.Core` | `ValenceControl.PackageCatalog.Core` |
| New extraction | `ValenceControl.PackageCatalog.Abstractions` |
| `ValenceControl.PackageCatalog.Sources.NuGet` | `ValenceControl.PackageCatalog.Sources.NuGet` |
| `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore` | `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore` |
| `ValenceControl.PackageCatalog.Persistence.SqliteMigrations` | `ValenceControl.PackageCatalog.Persistence.SqliteMigrations` |
| `ValenceControl.PackageCatalog.Persistence.SqlServerMigrations` | `ValenceControl.PackageCatalog.Persistence.SqlServerMigrations` |
| `ValenceControl.PackageCatalog.ServiceDefaults` | `ValenceControl.PackageCatalog.ServiceDefaults` |
| `ValenceControl.Console` | `ValenceControl.Console` |
| `ValenceControl.PackageManifests` | `ValenceControl.PackageManifests` |
| `ValenceControl.PackageManifest.Generator` | `ValenceControl.PackageManifest.Generator` |
| `ValenceControl.PackageManifest.Generator.Core` | `ValenceControl.PackageManifest.Generator.Core` |
| `ValenceControl.PackageManifest.Generator.MSBuild` | `ValenceControl.PackageManifest.Generator.MSBuild` |
| `ValenceControl.PackageCatalog.Core/Builder/*` | `ValenceControl.RuntimeBuilder.Core` and `ValenceControl.RuntimeBuilder.Abstractions` |
| `ValenceControl.PackageCatalog.Core/DeploymentTemplates/*` | `ValenceControl.RuntimeBuilder.DeploymentTemplates` |
| `ValenceControl.PackageCatalog.Core/RuntimeConfigurations/*` | `ValenceControl.RuntimeBuilder.Core` and `ValenceControl.RuntimeBuilder.Abstractions` |
| `ValenceControl.Api/Public/Builder/*` | Hosted in `ValenceControl.Api` until `ValenceControl.RuntimeBuilder.Api` packaging is justified |
| `ValenceControl.Api/Workspace/*RuntimeConfiguration*` | Hosted in `ValenceControl.Api` until `ValenceControl.RuntimeBuilder.Api` packaging is justified |
| `ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs` | Current catalog EF adapter for `ValenceControl.RuntimeBuilder.Abstractions.RuntimeConfigurations.IRuntimeConfigurationStore` |

## Compatibility Review

Before publishing renamed packages, check whether these existing package IDs are already consumed:

- `ValenceControl.PackageManifests`
- `ValenceControl.PackageManifest.Generator`

If consumed, choose one:

- Keep old package IDs while moving source projects under platform namespaces.
- Publish transitional packages that depend on or forward to new platform package IDs.
- Publish deprecation notices and migration guidance before removing old IDs.

## Spec Migration

Existing catalog specs should be imported under one of these strategies:

- Preserve as historical specs under `specs/catalog-archive/`.
- Rename into platform specs if implementation will continue from them.
- Link to old repo issue/spec references where history preservation is sufficient.

New specs from PR #36 must be imported and triaged:

- `009-server-bundle-generation`
- `010-runtime-image-metadata-api`
- `011-saved-runtime-configurations`
- `012-server-side-planning`
- `013-deployment-template-expansion`
- `014-byoc-deployment-targets`
- `015-managed-hosting-control-plane`
- `016-runtime-operations`
