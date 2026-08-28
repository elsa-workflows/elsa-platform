# Contract: Migration Map

## Current To Target Project Mapping

| Current project | Target project |
| --- | --- |
| `ElsaControl.Api` | `ElsaControl.Api` |
| `ElsaControl.PackageCatalog.AppHost` | `ElsaControl.PackageCatalog.AppHost` |
| `ElsaControl.PackageCatalog.Core` | `ElsaControl.PackageCatalog.Core` |
| New extraction | `ElsaControl.PackageCatalog.Abstractions` |
| `ElsaControl.PackageCatalog.Sources.NuGet` | `ElsaControl.PackageCatalog.Sources.NuGet` |
| `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore` | `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore` |
| `ElsaControl.PackageCatalog.Persistence.SqliteMigrations` | `ElsaControl.PackageCatalog.Persistence.SqliteMigrations` |
| `ElsaControl.PackageCatalog.Persistence.SqlServerMigrations` | `ElsaControl.PackageCatalog.Persistence.SqlServerMigrations` |
| `ElsaControl.PackageCatalog.ServiceDefaults` | `ElsaControl.PackageCatalog.ServiceDefaults` |
| `ElsaControl.Console` | `ElsaControl.Console` |
| `Elsa.Specifications.PackageManifests` | `Elsa.Specifications.PackageManifests` |
| `Elsa.Specifications.PackageManifest.Generator` | `Elsa.Specifications.PackageManifest.Generator` |
| `Elsa.Specifications.PackageManifest.Generator.Core` | `Elsa.Specifications.PackageManifest.Generator.Core` |
| `Elsa.Specifications.PackageManifest.Generator.MSBuild` | `Elsa.Specifications.PackageManifest.Generator.MSBuild` |
| `ElsaControl.PackageCatalog.Core/Builder/*` | `ElsaControl.RuntimeBuilder.Core` and `ElsaControl.RuntimeBuilder.Abstractions` |
| `ElsaControl.PackageCatalog.Core/DeploymentTemplates/*` | `ElsaControl.RuntimeBuilder.DeploymentTemplates` |
| `ElsaControl.PackageCatalog.Core/RuntimeConfigurations/*` | `ElsaControl.RuntimeBuilder.Core` and `ElsaControl.RuntimeBuilder.Abstractions` |
| `ElsaControl.Api/Public/Builder/*` | Hosted in `ElsaControl.Api` until `ElsaControl.RuntimeBuilder.Api` packaging is justified |
| `ElsaControl.Api/Workspace/*RuntimeConfiguration*` | Hosted in `ElsaControl.Api` until `ElsaControl.RuntimeBuilder.Api` packaging is justified |
| `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs` | Current catalog EF adapter for `ElsaControl.RuntimeBuilder.Abstractions.RuntimeConfigurations.IRuntimeConfigurationStore` |

## Compatibility Review

Before publishing renamed packages, check whether these existing package IDs are already consumed:

- `Elsa.Specifications.PackageManifests`
- `Elsa.Specifications.PackageManifest.Generator`

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
