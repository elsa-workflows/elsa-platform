# elsa-platform
Deployment platform for Elsa-based systems: manifests, artifacts, reconciliation, CLI, GitOps, and operator tooling.

## Platform Subsystems

Elsa Platform is the control-plane home for deployment and package governance capabilities.

Target subsystem layout:

```text
src/
  Elsa.Platform.Deployment.*
  Elsa.Platform.PackageCatalog.*
  Elsa.Platform.RuntimeBuilder.*
  Elsa.Platform.Console
  Elsa.Platform.PackageManifests
  Elsa.Platform.PackageManifest.Generator
  Elsa.Platform.PackageManifest.Generator.Core
  Elsa.Platform.PackageManifest.Generator.MSBuild
```

Package Catalog and Runtime Builder are sibling subsystems to Deployment. Deployment may validate package descriptors through catalog abstractions or client contracts, and may consume Runtime Builder artifacts/contracts where appropriate, but it must not depend on catalog API, persistence, or source-provider internals. The React console lives as a platform-level shell in `Elsa.Platform.Console` so deployment, package, runtime builder, and operations modules can share one console.

Implementation work is tracked through Spec Kit under `specs/`.
