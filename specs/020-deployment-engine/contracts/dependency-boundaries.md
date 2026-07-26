# Contract: Deployment Engine Dependency Boundaries

## Allowed Dependencies

`ValenceControl.Deployment.Engine` may reference:

- `ValenceControl.Deployment.Abstractions`
- .NET base class libraries
- `IArtifactReader.ReadResourcesAsync(...)`
- `DeploymentExecutionContext`

Test projects may reference:

- `ValenceControl.Deployment.Engine`
- `ValenceControl.Deployment.Abstractions`
- xUnit
- FluentAssertions

## Forbidden Dependencies

The engine package must not reference:

- `ValenceControl.Deployment.Cli`
- `ValenceControl.Deployment.Api`
- `ValenceControl.Deployment.Artifacts` concrete package
- `ValenceControl.Deployment.Manifest` concrete package
- Package Catalog implementation, persistence, API, or source packages
- Runtime Builder implementation or API packages
- ASP.NET hosting packages
- Entity Framework Core packages
- Kubernetes client packages
- OCI registry packages
- signing, approval, policy, or attestation packages

## Forbidden Behavioral Scope

The engine package must not:

- Parse manifests directly.
- Build or inspect artifacts directly.
- Expose CLI or HTTP endpoints.
- Persist history to a database.
- Resolve secrets.
- Start background reconciliation loops.
- Perform distributed locking.
- Reconcile workflow instances, bookmarks, execution state, logs, locks, queues, or transient runtime state.

## Required Boundary Tests

Boundary tests must verify:

- Source project references only allowed platform projects.
- Source code does not import forbidden namespaces or packages.
- Public API does not expose CLI, API, hosting, persistence, Kubernetes, OCI, signing, policy, or runtime-state types.
