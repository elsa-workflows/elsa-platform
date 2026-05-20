# Contract: Deployment Artifacts Dependency Boundaries

## Allowed Dependencies

`Elsa.Platform.Deployment.Artifacts` may reference:

- `Elsa.Platform.Deployment.Abstractions`
- `Elsa.Platform.Deployment.Manifest`
- .NET base class libraries
- `System.Text.Json`
- `System.IO.Compression`

Test projects may reference:

- `Elsa.Platform.Deployment.Artifacts`
- `Elsa.Platform.Deployment.Abstractions`
- `Elsa.Platform.Deployment.Manifest`
- xUnit
- FluentAssertions

## Forbidden Dependencies

The artifact package must not reference:

- `Elsa.Platform.Deployment.Engine`
- `Elsa.Platform.Deployment.Cli`
- `Elsa.Platform.Deployment.Api`
- Package Catalog implementation, persistence, API, or source packages
- Runtime Builder implementation or API packages
- ASP.NET hosting packages
- Entity Framework Core packages
- Kubernetes client packages
- OCI registry packages
- signing, policy, approval, or attestation packages

## Forbidden Behavioral Scope

The artifact package must not:

- Execute deployment plans.
- Validate target environments.
- Diff live state.
- Apply resources.
- Record deployment history.
- Resolve secrets.
- Load or execute arbitrary assemblies.
- Reconcile workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.

## Required Boundary Tests

Boundary tests must verify:

- Source project references only allowed platform projects.
- Source code does not import forbidden namespaces or packages.
- Artifact APIs expose diagnostics/result contracts rather than engine, CLI, API, hosting, or persistence contracts.
