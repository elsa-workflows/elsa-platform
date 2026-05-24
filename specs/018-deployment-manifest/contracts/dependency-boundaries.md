# Contract: Dependency Boundaries

## Allowed Dependencies

`Elsa.Platform.Deployment.Manifest` may reference:

- `Elsa.Platform.Deployment.Abstractions`.
- `System.Text.Json`.
- YAML parser package.

The test project may reference:

- `Elsa.Platform.Deployment.Manifest`.
- `Elsa.Platform.Deployment.Abstractions`.
- xUnit.
- FluentAssertions.

## Forbidden Dependencies

The manifest package must not reference:

- `Elsa.Platform.Deployment.Engine`
- `Elsa.Platform.Deployment.Cli`
- `Elsa.Platform.Deployment.Api`
- `Elsa.Platform.Deployment.Artifacts`
- `Elsa.Platform.Api`
- `Elsa.Platform.PackageCatalog.Core`
- `Elsa.Platform.PackageCatalog.Persistence.*`
- `Elsa.Platform.Console`
- `Elsa.Platform.RuntimeBuilder.Core`
- `Elsa.Platform.RuntimeBuilder.DeploymentTemplates`
- ASP.NET hosting packages
- Entity Framework packages
- UI packages

## Forbidden Behavior

The manifest package must not:

- Load workflow files.
- Read artifact folders or ZIP files.
- Contact a runtime API.
- Validate package compatibility through Package Catalog implementation.
- Reconcile workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.
