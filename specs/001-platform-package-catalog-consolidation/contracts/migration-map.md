# Contract: Migration Map

## Current To Target Project Mapping

| Current project | Target project |
| --- | --- |
| `Elsa.Catalog.Api` | `Elsa.Platform.PackageCatalog.Api` |
| `Elsa.Catalog.AppHost` | `Elsa.Platform.PackageCatalog.AppHost` |
| `Elsa.Catalog.Core` | `Elsa.Platform.PackageCatalog.Core` |
| New extraction | `Elsa.Platform.PackageCatalog.Abstractions` |
| `Elsa.Catalog.Packaging.NuGet` | `Elsa.Platform.PackageCatalog.Sources.NuGet` |
| `Elsa.Catalog.Persistence.EntityFrameworkCore` | `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore` |
| `Elsa.Catalog.Persistence.SqliteMigrations` | `Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations` |
| `Elsa.Catalog.Persistence.SqlServerMigrations` | `Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations` |
| `Elsa.Catalog.ServiceDefaults` | `Elsa.Platform.PackageCatalog.ServiceDefaults` |
| `Elsa.Catalog.AdminUi` | `Elsa.Platform.PackageCatalog.AdminUi` |
| `Elsa.PackageManifests` | `Elsa.Platform.PackageManifests` |
| `Elsa.PackageManifest.Generator` | `Elsa.Platform.PackageManifest.Generator` |
| `Elsa.PackageManifest.Generator.Core` | `Elsa.Platform.PackageManifest.Generator.Core` |
| `Elsa.PackageManifest.Generator.MSBuild` | `Elsa.Platform.PackageManifest.Generator.MSBuild` |
| `Elsa.Catalog.Core/Builder/*` | `Elsa.Platform.RuntimeBuilder.Core` |
| `Elsa.Catalog.Core/DeploymentTemplates/*` | `Elsa.Platform.RuntimeBuilder.DeploymentTemplates` |
| `Elsa.Catalog.Core/RuntimeConfigurations/*` | `Elsa.Platform.RuntimeBuilder.Core` or `Elsa.Platform.RuntimeBuilder.Abstractions` by contract role |
| `Elsa.Catalog.Api/Public/Builder/*` | `Elsa.Platform.RuntimeBuilder.Api` |
| `Elsa.Catalog.Api/Workspace/*RuntimeConfiguration*` | `Elsa.Platform.RuntimeBuilder.Api` |
| `Elsa.Catalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs` | `Elsa.Platform.RuntimeBuilder.Persistence.EntityFrameworkCore` |

## Compatibility Review

Before publishing renamed packages, check whether these existing package IDs are already consumed:

- `Elsa.PackageManifests`
- `Elsa.PackageManifest.Generator`

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
