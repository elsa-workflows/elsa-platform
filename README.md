# elsa-platform
Deployment platform for Elsa-based systems: manifests, artifacts, reconciliation, CLI, GitOps, and operator tooling.

## Platform Subsystems

Elsa Platform is the control-plane home for deployment and package governance capabilities.

Target subsystem layout:

```text
src/
  Elsa.Platform.Deployment.*
  Elsa.Platform.PackageCatalog.*
  Elsa.Platform.PackageManifests
  Elsa.Platform.PackageManifest.Generator
  Elsa.Platform.PackageManifest.Generator.Core
  Elsa.Platform.PackageManifest.Generator.MSBuild
```

Package Catalog is a sibling subsystem to Deployment. Deployment may validate package descriptors through catalog abstractions or client contracts, but it must not depend on catalog API, UI, persistence, or source-provider internals.

Implementation work is tracked through Spec Kit under `specs/`.
