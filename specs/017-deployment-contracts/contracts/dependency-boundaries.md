# Contract: Dependency Boundaries

## Allowed Dependencies

`ValenceControl.Deployment.Abstractions` may depend on:

- .NET base class library.

The test project may depend on:

- `ValenceControl.Deployment.Abstractions`.
- xUnit.
- xUnit's built-in assertions.
- Microsoft.NET.Test.Sdk.

## Forbidden Dependencies

`ValenceControl.Deployment.Abstractions` must not reference:

- `ValenceControl.Api`
- `ValenceControl.PackageCatalog.Core`
- `ValenceControl.Console`
- `ValenceControl.PackageCatalog.Persistence.*`
- `ValenceControl.PackageCatalog.Sources.*`
- `ValenceControl.RuntimeBuilder.Core`
- `ValenceControl.RuntimeBuilder.DeploymentTemplates`
- ASP.NET hosting packages
- Entity Framework packages
- migration projects
- UI projects
- CLI packages

Package Catalog abstractions remain an allowed future dependency only if a package descriptor validation slice proves it is necessary. This first contract slice does not require it.

## Forbidden Domain Vocabulary

Public deployment foundation contracts must not model data-plane runtime state:

- workflow instances
- bookmarks
- execution state
- execution logs
- locks
- queues
- transient runtime state

These words may appear in tests or docs only when asserting that they are excluded.

## Boundary Tests

The feature must include automated tests that:

- Inspect project references for `ValenceControl.Deployment.Abstractions`.
- Assert no forbidden project/package references are present.
- Search public source files for forbidden runtime-state vocabulary.
- Compile sample implementations of extension contracts without adding infrastructure dependencies.
