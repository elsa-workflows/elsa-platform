# Data Model: Valence Control Package Catalog Consolidation

## Valence Control Subsystem

Represents a bounded area inside `valence-control`.

Fields:

- `Name`: Deployment, PackageCatalog, PackageManifests, PackageManifestGenerator.
- `SourceProjects`: Projects owned by the subsystem.
- `TestProjects`: Tests owned by the subsystem.
- `AllowedDependencies`: Other subsystems or packages it may reference.
- `ForbiddenDependencies`: Projects it must not reference.
- `OwnerDocs`: Specs, ADRs, and README locations.

Validation rules:

- Deployment may reference catalog abstractions or client contracts only.
- Catalog may reference package manifests.
- Package manifests must not reference catalog, deployment, persistence, hosting, or generator implementation.

## Package Manifest Contract

Represents the shared `elsa-package.json` wire contract.

Fields:

- `SchemaVersion`
- `PackageId`
- `PackageVersion`
- `Features`
- `Settings`
- `Compatibility`
- `Dependencies`
- `Conflicts`
- `Licensing`
- `Documentation`
- `ExtensionData`

Validation rules:

- Schema version is independent from NuGet package version.
- Unknown extension data is preserved where supported.
- Contract package remains dependency-light.

## Catalog Source Provider

Represents a package metadata ingestion adapter.

Fields:

- `ProviderId`
- `SourceType`
- `Configuration`
- `DiscoveryPolicy`
- `AuthenticationCapabilities`
- `SafetyCapabilities`

Validation rules:

- NuGet provider must inspect package files and manifests without loading arbitrary assemblies.
- Source provider failures must be recorded as item-level sync diagnostics where possible.

## Runtime Builder Subsystem

Represents platform-owned runtime composition and bundle generation capabilities.

Fields:

- `BuilderIntent`
- `RuntimeImageMetadata`
- `RuntimePlan`
- `BundleFiles`
- `BundleFindings`
- `DeploymentTemplateTarget`
- `SavedRuntimeConfiguration`
- `RuntimeConfigurationVersion`

Validation rules:

- Runtime Builder may consume Package Catalog abstractions or client contracts.
- Runtime Builder must not depend on catalog EF persistence.
- Generated deployment templates are files/previews, not live deployment reconciliation.
- Bundle output must not expose raw secrets.

## Compatibility Contract

Represents catalog data needed by deployment validation and builder clients.

Fields:

- `PackageId`
- `Version`
- `Exists`
- `Listed`
- `Valid`
- `Approved`
- `Trusted`
- `Suspicious`
- `CompatibilityStatus`
- `Errors`
- `Warnings`

Validation rules:

- Validity, approval, trust, and compatibility must not be collapsed into one boolean.
- Missing packages and unsupported versions are explicit outcomes.

## Migration Phase

Represents a tracked implementation phase.

Fields:

- `PhaseNumber`
- `Name`
- `EntryCriteria`
- `Tasks`
- `Verification`
- `ExitCriteria`
- `DecisionGate`

State transitions:

```text
NotStarted -> InProgress -> Blocked
NotStarted -> InProgress -> Complete
Blocked -> InProgress
Complete -> Reopened
```

## Deprecation Notice

Represents the old repository shutdown state.

Fields:

- `RepositoryUrl`
- `ReplacementUrl`
- `ReadmeUpdated`
- `IssuesMigrated`
- `SpecsMigrated`
- `Archived`
- `ArchiveDate`

Validation rules:

- Old repository cannot be archived until platform catalog is usable and unresolved work is triaged.
