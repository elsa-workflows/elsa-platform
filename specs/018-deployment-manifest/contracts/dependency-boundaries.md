# Contract: Dependency Boundaries

## Allowed Dependencies

`ValenceControl.Deployment.Manifest` may reference:

- `ValenceControl.Deployment.Abstractions`.
- `System.Text.Json`.
- YAML parser package.

The test project may reference:

- `ValenceControl.Deployment.Manifest`.
- `ValenceControl.Deployment.Abstractions`.
- xUnit.
- FluentAssertions.

## Forbidden Dependencies

The manifest package must not reference:

- `ValenceControl.Deployment.Engine`
- `ValenceControl.Deployment.Cli`
- `ValenceControl.Deployment.Api`
- `ValenceControl.Deployment.Artifacts`
- `ValenceControl.Api`
- `ValenceControl.PackageCatalog.Core`
- `ValenceControl.PackageCatalog.Persistence.*`
- `ValenceControl.Console`
- `ValenceControl.RuntimeBuilder.Core`
- `ValenceControl.RuntimeBuilder.DeploymentTemplates`
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
