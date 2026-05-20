# Contract: Migration Map

## Current To Target Project Mapping

| Current project | Target project |
| --- | --- |
| `Elsa.Platform.PackageCatalog.Api` | `Elsa.Platform.PackageCatalog.Api` |
| `Elsa.Platform.PackageCatalog.AppHost` | `Elsa.Platform.PackageCatalog.AppHost` |
| `Elsa.Platform.PackageCatalog.Core` | `Elsa.Platform.PackageCatalog.Core` |
| New extraction | `Elsa.Platform.PackageCatalog.Abstractions` |
| `Elsa.Platform.PackageCatalog.Sources.NuGet` | `Elsa.Platform.PackageCatalog.Sources.NuGet` |
| `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore` | `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore` |
| `Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations` | `Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations` |
| `Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations` | `Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations` |
| `Elsa.Platform.PackageCatalog.ServiceDefaults` | `Elsa.Platform.PackageCatalog.ServiceDefaults` |
| `Elsa.Platform.PackageCatalog.AdminUi` | `Elsa.Platform.PackageCatalog.AdminUi` |
| `Elsa.Platform.PackageManifests` | `Elsa.Platform.PackageManifests` |
| `Elsa.Platform.PackageManifest.Generator` | `Elsa.Platform.PackageManifest.Generator` |
| `Elsa.Platform.PackageManifest.Generator.Core` | `Elsa.Platform.PackageManifest.Generator.Core` |
| `Elsa.Platform.PackageManifest.Generator.MSBuild` | `Elsa.Platform.PackageManifest.Generator.MSBuild` |
| `Elsa.Platform.PackageCatalog.Core/Builder/*` | `Elsa.Platform.RuntimeBuilder.Core` and `Elsa.Platform.RuntimeBuilder.Abstractions` |
| `Elsa.Platform.PackageCatalog.Core/DeploymentTemplates/*` | `Elsa.Platform.RuntimeBuilder.DeploymentTemplates` |
| `Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/*` | `Elsa.Platform.RuntimeBuilder.Core` and `Elsa.Platform.RuntimeBuilder.Abstractions` |
| `Elsa.Platform.PackageCatalog.Api/Public/Builder/*` | Hosted in `Elsa.Platform.PackageCatalog.Api` until `Elsa.Platform.RuntimeBuilder.Api` packaging is justified |
| `Elsa.Platform.PackageCatalog.Api/Workspace/*RuntimeConfiguration*` | Hosted in `Elsa.Platform.PackageCatalog.Api` until `Elsa.Platform.RuntimeBuilder.Api` packaging is justified |
| `Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs` | Current catalog EF adapter for `Elsa.Platform.RuntimeBuilder.Abstractions.RuntimeConfigurations.IRuntimeConfigurationStore` |

## Compatibility Review

Before publishing renamed packages, check whether these existing package IDs are already consumed:

- `Elsa.Platform.PackageManifests`
- `Elsa.Platform.PackageManifest.Generator`

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
