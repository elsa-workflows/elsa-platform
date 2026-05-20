# Contract: Dependency Boundaries

## Allowed Dependencies

`Elsa.Platform.Deployment.Abstractions` may depend on:

- .NET base class library.

The test project may depend on:

- `Elsa.Platform.Deployment.Abstractions`.
- xUnit.
- FluentAssertions.
- Microsoft.NET.Test.Sdk.

## Forbidden Dependencies

`Elsa.Platform.Deployment.Abstractions` must not reference:

- `Elsa.Platform.PackageCatalog.Api`
- `Elsa.Platform.PackageCatalog.Core`
- `Elsa.Platform.AdminUi`
- `Elsa.Platform.PackageCatalog.Persistence.*`
- `Elsa.Platform.PackageCatalog.Sources.*`
- `Elsa.Platform.RuntimeBuilder.Core`
- `Elsa.Platform.RuntimeBuilder.DeploymentTemplates`
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

- Inspect project references for `Elsa.Platform.Deployment.Abstractions`.
- Assert no forbidden project/package references are present.
- Search public source files for forbidden runtime-state vocabulary.
- Compile sample implementations of extension contracts without adding infrastructure dependencies.
