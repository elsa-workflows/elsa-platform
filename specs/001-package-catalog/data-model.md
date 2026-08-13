# Data Model: Valence Control Package Catalog

## Overview

The catalog stores durable source configuration, package identities, immutable
package versions, raw manifests, derived feature projections, validation results,
approval decisions, and sync history. All timestamps are UTC. Public queries
filter to package versions that are valid, approved, listed, and not suspicious.

## Enums And Value Objects

### PackageSourceType

- `NuGetFeed`

### PackageSourceApprovalPolicy

- `AutoApprove`
- `Manual`

### PackageSourceVersionDiscoveryPolicy

- `AllVersions`: discover and evaluate every version returned by the source.
- `LatestStable`: discover only the highest non-prerelease version per package.
- `LatestIncludingPrerelease`: discover only the highest version per package,
  including prerelease versions.
- `LatestPreview`: discover only the highest prerelease version per package
  whose prerelease label equals `preview`, or starts with `preview.` or
  `preview-`, case-insensitively.

### PackageApprovalStatus

- `Pending`
- `Approved`
- `Rejected`

### ValidationStatus

- `NotValidated`
- `Valid`
- `Invalid`
- `UnsupportedSchema`
- `Suspicious`

### SyncRunTrigger

- `Scheduled`
- `ManualAll`
- `ManualSource`
- `ManualPackage`

### SyncRunStatus

- `Running`
- `Completed`
- `Failed`
- `CompletedWithErrors`

### SyncRunItemStatus

- `Discovered`
- `Skipped`
- `Downloaded`
- `Indexed`
- `Unchanged`
- `Invalid`
- `Failed`
- `Suspicious`

### ManifestHash

String value computed from the raw manifest bytes after deterministic manifest
normalization chosen by implementation. Used only for change detection, not as a
security signature.

### SemVerRange

String value validated as a version range for Elsa versions, Docker image
versions, and package compatibility declarations.

## Entities

### PackageSource

Configured source to scan.

Fields:

- `Id`: stable identifier
- `Name`: administrator-facing source name
- `Type`: `NuGetFeed`
- `Url`: feed URL
- `Enabled`: whether scheduled sync includes this source
- `IncludePatterns`: case-insensitive glob patterns
- `ExcludePatterns`: case-insensitive glob patterns; takes precedence
- `ApprovalPolicy`: `AutoApprove` or `Manual`
- `VersionDiscoveryPolicy`: `AllVersions`, `LatestStable`,
  `LatestIncludingPrerelease`, or `LatestPreview`
- `LastSyncedAt`: last completed sync timestamp, nullable
- `CreatedAt`: UTC timestamp
- `UpdatedAt`: UTC timestamp

Relationships:

- Has many `Package`
- Has many `SyncRunItem`

Validation:

- `Name` is required and unique enough for admin display.
- `Url` is required and must be absolute.
- At least one include pattern is required.
- Exclude patterns are optional.
- Version discovery defaults to `AllVersions` for existing sources.

### Package

Source-scoped NuGet package identity.

Fields:

- `Id`: stable identifier
- `PackageId`: NuGet package ID
- `SourceId`: owning source
- `Approved`: package-level approval flag for backward-compatible public checks
- `Listed`: package-level listing flag
- `LatestVersion`: latest public visible version, nullable
- `CreatedAt`: UTC timestamp
- `UpdatedAt`: UTC timestamp

Relationships:

- Belongs to `PackageSource`
- Has many `PackageVersion`
- Has many package-level `ApprovalRecord`

Validation:

- `(SourceId, PackageId)` is unique.
- `PackageId` must match NuGet package ID rules accepted by NuGet client APIs.

### PackageVersion

Immutable NuGet package version record.

Fields:

- `Id`: stable identifier
- `PackageId`: owning package record
- `Version`: NuGet package version string
- `ManifestJson`: raw manifest JSON as extracted
- `ManifestHash`: hash of manifest content
- `NuspecJson`: optional JSON snapshot of relevant nuspec metadata
- `PublishedAt`: package publication timestamp when available
- `IndexedAt`: UTC timestamp when first indexed
- `ValidationStatus`: validation state
- `ValidationErrors`: denormalized validation error summary for admin listings
- `ApprovalStatus`: version-level approval state
- `IsListed`: version-level listing flag
- `SuspiciousChangeDetected`: true when later sync sees different manifest hash
- `SuspiciousManifestHash`: newly observed mismatched hash, nullable
- `SchemaVersion`: manifest schema version when parseable

Relationships:

- Belongs to `Package`
- Has one or more `ManifestValidationResult`
- Has many derived `FeatureRecord`
- Has many version-level `ApprovalRecord`
- Appears in many `SyncRunItem`

Validation:

- `(PackageId, Version)` is unique within a source-scoped package.
- Existing `ManifestJson` and `ManifestHash` are not overwritten after indexing.
- Suspicious changes update suspicious fields and sync diagnostics only.

### ManifestValidationResult

Persisted validation outcome for a package version.

Fields:

- `Id`: stable identifier
- `PackageVersionId`: version under validation
- `SchemaVersion`: declared schema version when parseable
- `Status`: validation status
- `ErrorsJson`: serialized validation errors
- `WarningsJson`: serialized validation warnings
- `ValidatedAt`: UTC timestamp
- `ValidatorVersion`: version of validator/schema package, nullable

Relationships:

- Belongs to `PackageVersion`

Validation:

- Errors and warnings include field path, rule ID, message, and severity.

### FeatureRecord

Query projection for a feature declared by a valid or invalid manifest.

Fields:

- `Id`: stable identifier
- `PackageVersionId`: declaring package version
- `FeatureId`: manifest feature ID
- `TypeName`: CLR type name
- `DisplayName`: display name
- `Description`: description
- `Category`: category
- `RequiredCapabilitiesJson`: serialized required capabilities
- `DependenciesJson`: serialized feature dependencies
- `ConflictsJson`: serialized feature conflicts
- `InfrastructureJson`: serialized abstract infrastructure requirements
- `Advanced`: advanced flag
- `Experimental`: experimental flag
- `ExtensionsJson`: extension metadata projection when needed

Relationships:

- Belongs to `PackageVersion`
- Has many `FeatureSettingRecord`

Validation:

- `(PackageVersionId, FeatureId)` is unique.
- Feature IDs must be stable within a package version.

### FeatureSettingRecord

Query projection for a feature setting.

Fields:

- `Id`: stable identifier
- `FeatureRecordId`: declaring feature
- `Name`: setting name
- `ClrType`: CLR type name
- `JsonType`: JSON type
- `Required`: required flag
- `DefaultValueJson`: default value when known
- `DisplayName`: display name
- `Description`: description
- `Category`: category or group
- `ValidationJson`: validation metadata
- `Secret`: secret or sensitive flag
- `RestartRequired`: restart-required flag
- `EnvironmentVariable`: environment variable mapping
- `UiJson`: UI hints
- `ExtensionsJson`: extension metadata projection when needed

Relationships:

- Belongs to `FeatureRecord`

Validation:

- `(FeatureRecordId, Name)` is unique.
- Secret settings must not expose default values in public responses unless the
  manifest explicitly marks a safe placeholder value.

### ApprovalRecord

Auditable admin decision.

Fields:

- `Id`: stable identifier
- `TargetType`: `Package` or `PackageVersion`
- `TargetId`: target record ID
- `Status`: `Approved` or `Rejected`
- `Reason`: optional decision note
- `Actor`: admin identity or API key name
- `CreatedAt`: UTC timestamp

Relationships:

- Targets package or package version.

Validation:

- New decisions do not delete earlier decisions.
- Current approval state is derived from the latest applicable decision plus
  source policy defaults.

### SyncRun

Top-level sync execution.

Fields:

- `Id`: stable identifier
- `Trigger`: scheduled or manual trigger type
- `Status`: running/completed/failed status
- `StartedAt`: UTC timestamp
- `CompletedAt`: UTC timestamp, nullable
- `Error`: top-level error, nullable
- `SummaryCountersJson`: discovered, downloaded, indexed, skipped, invalid,
  failed, approved, pending, unchanged, and suspicious counters

Relationships:

- Has many `SyncRunItem`

Validation:

- Running syncs have no completed timestamp.
- Completed syncs have a completed timestamp.

### SyncRunItem

Item-level sync diagnostic.

Fields:

- `Id`: stable identifier
- `SyncRunId`: owning run
- `SourceId`: package source, nullable for global failures
- `PackageId`: NuGet package ID, nullable
- `Version`: NuGet version, nullable
- `PackageVersionId`: indexed package version, nullable
- `Status`: item status
- `Message`: short status message
- `Error`: detailed error, nullable
- `WarningsJson`: warnings, nullable
- `StartedAt`: UTC timestamp
- `CompletedAt`: UTC timestamp, nullable

Relationships:

- Belongs to `SyncRun`
- May reference `PackageSource`
- May reference `PackageVersion`

Validation:

- Failed and invalid items must include error or validation details.
- Suspicious items must include the observed mismatched hash.

## Manifest Contract Types

### ElsaPackageManifest

Wire contract root. Fields:

- `schemaVersion`
- `package`
- `displayName`
- `description`
- `tags`
- `features`
- `compatibility`
- `dependencies`
- `conflicts`
- `license`
- `documentation`
- `extensions`

### FeatureManifest

Feature declaration. Fields:

- `id`
- `typeName`
- `displayName`
- `description`
- `category`
- `settings`
- `dependencies`
- `conflicts`
- `requiredCapabilities`
- `advanced`
- `experimental`
- `extensions`

### FeatureSettingManifest

Setting declaration. Fields:

- `name`
- `clrType`
- `jsonType`
- `required`
- `defaultValue`
- `displayName`
- `description`
- `category`
- `validation`
- `secret`
- `restartRequired`
- `environmentVariable`
- `ui`
- `extensions`

### CompatibilityManifest

Compatibility declaration. Fields:

- `elsaVersionRange`
- `dockerImageVersionRange`
- `packageRules`
- `runtimeCapabilities`
- `extensions`

### DependencyManifest

Dependency declaration. Fields:

- `packageId`
- `versionRange`
- `featureId`
- `optional`
- `reason`
- `extensions`

### ConflictManifest

Conflict declaration. Fields:

- `packageId`
- `versionRange`
- `featureId`
- `reason`
- `extensions`

### LicenseManifest

License declaration. Fields:

- `expression`
- `url`
- `requiresAcceptance`
- `extensions`

### DocumentationManifest

Documentation declaration. Fields:

- `readmeUrl`
- `projectUrl`
- `releaseNotesUrl`
- `configurationUrl`
- `extensions`

## State Transitions

### Package Version Ingestion

1. `Discovered`: package ID and version found on a source.
2. `Downloaded`: package archive retrieved.
3. `ManifestMissing` or `ManifestExtracted`: manifest read attempt completed.
4. `Invalid` or `Valid`: validation completed.
5. `Pending`, `Approved`, or `Rejected`: approval state assigned.
6. `Listed` or `Unlisted`: listing state assigned.
7. `Suspicious`: later sync found mismatched manifest hash.

### Approval

- `Pending` -> `Approved`
- `Pending` -> `Rejected`
- `Approved` -> `Rejected`
- `Rejected` -> `Approved`

Approval changes never imply validation changes.

### Sync Run

- `Running` -> `Completed`
- `Running` -> `CompletedWithErrors`
- `Running` -> `Failed`

Item-level failures should produce `CompletedWithErrors` unless the run cannot
continue at all.

## Public Visibility Rule

A package version is public only when all conditions are true:

- Package is listed.
- Package version is listed.
- Package-level approval is approved or source policy permits visibility.
- Package-version approval is approved.
- Validation status is valid.
- Suspicious change is not active.

Admin APIs may read all states.
