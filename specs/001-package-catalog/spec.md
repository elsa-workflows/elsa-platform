# Feature Specification: Elsa Package Catalog

**Feature Branch**: `001-package-catalog`

**Created**: 2026-05-14

**Status**: Draft

**Input**: User description: "Create a specification for an ASP.NET Core application called Elsa Package Catalog together with a shared manifest contract package called Elsa.Platform.PackageManifests."

## Overview

Elsa Package Catalog provides a discoverable catalog of approved NuGet packages for professional Elsa Docker images and the future Elsa Runtime Builder UI. It indexes configured package sources, extracts generated `elsa-package.json` manifests from NuGet packages, validates those manifests, persists package metadata, and exposes read APIs for package, version, feature, settings, compatibility, validation, and approval information.

The feature also defines `Elsa.Platform.PackageManifests`, a shared wire contract package used by the future manifest generator, catalog service, runtime validation, and Runtime Builder tooling. The contract package defines versioned manifest DTOs, schema constants, JSON serialization behavior, validation abstractions, compatibility models, extension data support, and embedded schema resources where appropriate. It must remain independent from catalog persistence and runtime infrastructure.

The first catalog implementation is a modular monolith with separate public and admin APIs, durable local storage, periodic source synchronization, admin-triggered syncs, approval workflow support, and immutable package-version handling. The system must never execute package code or load arbitrary package assemblies; it only inspects package files and manifests.

## Clarifications

### Session 2026-05-14

- Q: Should private NuGet feed credentials be included in the first implementation or deferred? -> A: Defer private feed credentials; v1 supports unauthenticated NuGet feeds only.
- Q: Should package approval automatically approve future versions from manual sources? -> A: Every new version from a manual source remains pending until explicitly approved.
- Q: What maximum manifest size should be accepted before validation rejects the package version? -> A: 1 MB maximum manifest size.
- Q: Should public APIs expose validation warnings for otherwise valid approved packages? -> A: Public discovery hides validation warnings; compatibility checks may return relevant warnings.
- Q: Which package ID pattern syntax should administrators use first? -> A: Case-insensitive glob patterns, with excludes taking precedence.

### Session 2026-05-16

- Q: How should broad package source sync avoid long admin request timeouts? -> A: Manual sync requests enqueue a durable sync run and return immediately; package discovery and downloads continue in a background worker independent of the HTTP request lifetime.
- Q: How should preview feeds avoid branch or custom prerelease labels? -> A: Sources can select the latest preview-only package version, defined as the highest NuGet prerelease version whose prerelease label equals `preview` or starts with `preview.`/`preview-`, case-insensitively.

## Goals

- Define a stable, independently versioned manifest contract that supports forward-compatible evolution.
- Provide a public package and feature discovery surface for future Elsa runtime composition tooling.
- Index explicitly configured NuGet package sources using include and exclude patterns.
- Extract and validate `elsa-package.json` manifests from matching package versions.
- Store package sources, packages, versions, raw manifests, validation results, approval state, and sync history.
- Support scheduled sync and manual sync for all sources, one source, or one package.
- Keep package version records immutable and flag changed manifests for the same package ID and version as suspicious.
- Expose only listed, approved, valid package versions through public APIs.
- Expose invalid, unapproved, rejected, and suspicious versions through protected admin APIs.
- Provide enough package, feature, setting, and compatibility metadata for a future UI that lets users select packages and configure features.

## Non-Goals

- Building the Runtime Builder UI.
- Implementing the build-time manifest generator.
- Installing packages into Docker containers.
- Running Nuplane.
- Generating deployment bundles or final `config.json` files.
- Executing package code or loading package assemblies.
- License validation with Sigil.
- Full dependency resolution.
- Full package signing verification.
- Distributed synchronization infrastructure.
- Redis or any cache as primary storage.

## Personas

- **Runtime Builder User**: Discovers approved packages and features to assemble an Elsa runtime configuration.
- **Catalog Administrator**: Configures sources, triggers syncs, approves or rejects packages and versions, and investigates validation results.
- **Package Publisher**: Publishes NuGet packages containing generated Elsa package manifests and expects them to become discoverable after approval.
- **Runtime Validator**: Consumes shared manifest contract metadata to validate selected packages and features before runtime use.
- **Manifest Tooling Developer**: Uses the shared contract package as the canonical manifest model without depending on catalog persistence or runtime internals.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover Approved Packages (Priority: P1)

A Runtime Builder user can browse available packages, inspect versions, and view features and configurable settings that are approved for professional Elsa Docker images.

**Why this priority**: Public discovery is the primary value of the catalog and enables the future Runtime Builder experience.

**Independent Test**: Seed the catalog with approved, listed, valid package versions and verify that public package, version, and feature queries return only those records with enough metadata to support package selection and feature configuration.

**Acceptance Scenarios**:

1. **Given** a package has one listed, approved, valid version and one invalid version, **When** a user requests public package details, **Then** only the valid approved version is visible.
2. **Given** a manifest declares features and feature settings, **When** a user requests feature details, **Then** the response includes feature identity, descriptions, settings schema metadata, dependency and conflict metadata, and compatibility information.
3. **Given** a package version is rejected or unlisted, **When** a user searches the public catalog, **Then** that package version is omitted.

---

### User Story 2 - Configure Package Sources (Priority: P1)

A Catalog Administrator can configure NuGet package sources with explicit include and exclude patterns so the catalog indexes only approved candidate package IDs instead of scanning broad feeds blindly.

**Why this priority**: Source configuration determines what the catalog can safely and efficiently index.

**Independent Test**: Create, update, list, and delete a source, then verify that sync eligibility respects enabled state, source type, URL, include patterns, exclude patterns, and approval policy.

**Acceptance Scenarios**:

1. **Given** an administrator creates a source with include patterns and manual approval policy, **When** the source is saved, **Then** it is available for future sync runs with the same configuration.
2. **Given** a source is disabled, **When** scheduled synchronization runs, **Then** the source is skipped and no packages are indexed from it.
3. **Given** include and exclude patterns both match a package ID, **When** packages are selected for indexing, **Then** the exclude pattern wins.

---

### User Story 3 - Synchronize Manifests (Priority: P1)

The catalog can periodically and manually synchronize configured package sources, discover matching package versions, extract manifests, validate them, and persist results without one failed package stopping the entire run.

**Why this priority**: Synchronization is the data ingestion path that makes the catalog useful and trustworthy.

**Independent Test**: Run a sync against a controlled package source containing valid, invalid, missing-manifest, and duplicate-version packages, then verify persisted package versions, validation results, sync run items, and summary counters.

**Acceptance Scenarios**:

1. **Given** a matching package version contains a valid manifest, **When** sync processes it, **Then** the raw manifest, manifest hash, validation status, approval status, and indexed timestamp are stored.
2. **Given** a package version does not contain a manifest, **When** sync processes it, **Then** the version is stored or recorded with a validation failure that is visible to administrators and hidden from public APIs.
3. **Given** one package download fails during a sync run, **When** other package versions remain processable, **Then** the run completes with errors and records item-level failure details.
4. **Given** a package ID and version was previously indexed with one manifest hash, **When** the same package ID and version is encountered with different manifest content, **Then** the existing manifest is not silently replaced and the version is marked suspicious for admin review.

---

### User Story 4 - Approve Catalog Entries (Priority: P2)

A Catalog Administrator can review packages and versions, approve or reject them, and keep approval separate from manifest validity.

**Why this priority**: Third-party package discovery requires explicit trust decisions that are independent from technical manifest correctness.

**Independent Test**: Index package versions under both auto-approve and manual policies, approve and reject package-level and version-level records, and verify public API visibility changes accordingly.

**Acceptance Scenarios**:

1. **Given** a source uses manual approval, **When** a valid package version is indexed, **Then** it remains hidden from public APIs until approved.
2. **Given** an administrator approves a package but rejects a specific version, **When** public package versions are queried, **Then** the rejected version is omitted.
3. **Given** an invalid manifest is approved by mistake, **When** public APIs are queried, **Then** the invalid version is still hidden because validity is required separately from approval.

---

### User Story 5 - Check Compatibility (Priority: P2)

A Runtime Builder or validation client can submit a selected package set and target runtime metadata to receive a compatibility result that explains whether the selection is usable.

**Why this priority**: Compatibility feedback is needed before a future UI can confidently compose runtime configurations.

**Independent Test**: Submit package selections with existing, missing, unapproved, invalid, compatible, and incompatible versions and verify the result contains deterministic pass, warning, and error outcomes.

**Acceptance Scenarios**:

1. **Given** a selected package version exists, is listed, approved, valid, and declares an Elsa version range that includes the requested Elsa version, **When** compatibility is checked, **Then** the package passes the first-version compatibility checks.
2. **Given** a selected package version is missing, unapproved, rejected, unlisted, or invalid, **When** compatibility is checked, **Then** the result contains an error for that package.
3. **Given** a manifest omits optional compatibility ranges, **When** compatibility is checked, **Then** the result reports the absence as unknown or warning rather than inventing compatibility.

---

### User Story 6 - Share Manifest Contracts (Priority: P2)

Manifest tooling, catalog ingestion, and future runtime validation can all use one shared contract for manifest JSON without referencing catalog persistence or runtime infrastructure.

**Why this priority**: A stable contract prevents drift between generator, catalog, and runtime tooling.

**Independent Test**: Serialize and deserialize representative manifests, including unknown extension data and future fields, and verify contract validation behavior remains stable across package consumers.

**Acceptance Scenarios**:

1. **Given** a manifest includes recognized fields and custom extension metadata, **When** it is deserialized and serialized through the shared contract, **Then** recognized fields are strongly typed and extension metadata is preserved.
2. **Given** a manifest declares an unsupported schema version, **When** validation runs, **Then** the result clearly identifies the schema version issue without losing the raw manifest.
3. **Given** future tooling adds extension metadata under approved extension locations, **When** current catalog ingestion processes it, **Then** unknown extension data is preserved and does not fail validation solely because it is unknown.

### Edge Cases

- Package source URL is unreachable, returns malformed metadata, or requires credentials; credential-protected sources are rejected or skipped in v1 because only unauthenticated feeds are supported.
- Include patterns match no packages.
- Exclude patterns eliminate all included packages.
- A NuGet package contains no `elsa-package.json`.
- A NuGet package contains multiple candidate manifest files.
- Manifest package identity or version does not match the NuGet package identity or version.
- Manifest JSON is malformed, exceeds the 1 MB v1 size limit, or uses an unsupported schema version.
- Manifest validates structurally but references malformed version ranges.
- Package dependencies, feature dependencies, or conflicts refer to packages or features not currently indexed.
- The same source discovers the same package version more than once.
- Different sources expose the same package ID and version with different manifest content.
- Public APIs are queried while a sync run is active.
- Manual sync is requested for a source or package that is already being synchronized.
- Admin approval changes while public clients are reading cached responses.
- Time-based fields are supplied in local time or without timezone information.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define a shared manifest contract package named `Elsa.Platform.PackageManifests` that represents the manifest wire contract only.
- **FR-002**: The shared manifest contract MUST NOT depend on catalog persistence concerns, runtime installation internals, Nuplane execution, or arbitrary package assembly loading.
- **FR-003**: The shared manifest contract MUST define strongly typed DTOs for `ElsaPackageManifest`, `FeatureManifest`, `FeatureSettingManifest`, `CompatibilityManifest`, `DependencyManifest`, `ConflictManifest`, `LicenseManifest`, `DocumentationManifest`, and validation result types.
- **FR-004**: The shared manifest contract MUST expose manifest schema version constants and identify the currently supported schema version.
- **FR-005**: Manifest DTOs MUST preserve unknown extension data at package, feature, setting, compatibility, dependency, conflict, license, documentation, and validation result levels where future metadata may be added.
- **FR-006**: Manifest serialization MUST use stable property names and deterministic serialization behavior suitable for JSON wire exchange and hashing.
- **FR-007**: Manifest validation helpers MUST validate schema version support, required fields, structural JSON Schema rules, package identity consistency, version syntax, compatibility range syntax, dependency references, and conflict declarations.
- **FR-008**: The manifest contract MUST support embedded or discoverable versioned JSON Schema resources.
- **FR-009**: The manifest contract MUST support a validation result model with status, errors, warnings, field paths, rule identifiers, and extension metadata.
- **FR-010**: An Elsa package manifest MUST include schema version, package ID, package version, display name, description, tags, features, compatibility metadata, licensing metadata, documentation links, package dependencies, package conflicts, and extension metadata.
- **FR-011**: A feature manifest MUST include feature ID, CLR type name, display name, description, category, settings schema, dependencies, conflicts, required capabilities, advanced flag, experimental flag, and extension metadata.
- **FR-012**: A feature setting manifest MUST include setting name, CLR type, JSON type, required flag, default value when known, display name, description, category or group, validation metadata, secret or sensitive flag, restart-required flag, environment variable mapping, UI hints, and extension metadata.
- **FR-013**: Compatibility metadata MUST support Elsa version ranges, Docker image version ranges, package compatibility rules, runtime capability requirements, and future compatibility check metadata.
- **FR-014**: The system MUST use a versioned JSON Schema strategy where schema versions can evolve independently from NuGet package versions.
- **FR-015**: The system MUST recommend `elsa-package.json` at the NuGet package root as the canonical manifest path and MAY support a documented well-known fallback path of `build/elsa-package.json`.
- **FR-016**: If multiple manifests are found in one package version, the system MUST prefer the canonical root manifest and record a validation warning for additional manifest files.
- **FR-017**: The catalog MUST allow administrators to create, read, update, and delete configured package sources.
- **FR-018**: Package sources MUST contain ID, name, type, URL, enabled flag, include patterns, exclude patterns, approval policy, last synced timestamp, created timestamp, and updated timestamp.
- **FR-019**: Package source type MUST initially support NuGet feed sources.
- **FR-020**: Package source approval policy MUST support at least `AutoApprove` and `Manual`.
- **FR-021**: The catalog MUST scan only explicitly configured package sources and MUST select package IDs by include and exclude patterns rather than broad blind feed scanning.
- **FR-021a**: The first version MUST support unauthenticated NuGet feeds only and MUST defer private feed credentials to a later explicitly scoped feature.
- **FR-021b**: Package source include and exclude patterns MUST use case-insensitive glob syntax in the first version, and exclude patterns MUST take precedence over include patterns.
- **FR-022**: The catalog MUST periodically scan enabled sources.
- **FR-023**: Administrators MUST be able to trigger sync for all sources, one source, or one package.
- **FR-023a**: Manual sync trigger APIs MUST return a persisted sync run promptly and MUST execute package discovery and downloads in a background worker that is not canceled when the admin HTTP request ends.
- **FR-024**: Synchronization MUST discover available versions for matching packages.
- **FR-024a**: Package sources MUST support a version discovery policy that can select all versions, the latest stable version, the latest version including prerelease versions, or the latest preview-only prerelease version.
- **FR-024b**: The latest preview-only policy MUST ignore stable versions and non-preview prerelease labels such as release candidates or branch-named prereleases.
- **FR-025**: Synchronization MUST download only new package versions by default.
- **FR-026**: Synchronization MUST support a forced reindex mode for admin operations while preserving immutable version handling.
- **FR-027**: Synchronization MUST extract `elsa-package.json` from packages without executing package code or loading package assemblies.
- **FR-028**: Synchronization MUST store raw manifest JSON for every extracted manifest regardless of validation outcome.
- **FR-029**: Synchronization MUST compute and store a manifest hash for every extracted manifest.
- **FR-030**: Package version records MUST be treated as immutable once indexed.
- **FR-031**: If the same package ID and version is encountered with a different manifest hash, the system MUST mark the version or sync item as suspicious and MUST NOT silently overwrite the existing manifest.
- **FR-032**: Manifest validation failures MUST be persisted and inspectable through admin APIs.
- **FR-033**: A validation failure for one package version MUST NOT fail the entire sync run.
- **FR-033a**: The first version MUST reject extracted manifests larger than 1 MB as validation failures.
- **FR-034**: The catalog MUST store package, package version, feature, feature setting, manifest validation result, source, sync run, sync run item, and approval metadata sufficient to support public and admin APIs.
- **FR-035**: Packages MUST contain ID, package ID, source ID, approved flag, listed flag, latest visible version, created timestamp, and updated timestamp.
- **FR-036**: Package versions MUST contain ID, package reference, version, raw manifest JSON, manifest hash, optional NuGet metadata snapshot, published timestamp when available, indexed timestamp, validation status, validation errors, approval status, listed flag, and suspicious-change status.
- **FR-037**: Sync runs MUST contain ID, trigger, status, started timestamp, completed timestamp, error summary, and summary counters.
- **FR-038**: Sync run items MUST record per-source, per-package, or per-version outcomes including skipped, indexed, unchanged, invalid, failed, and suspicious states.
- **FR-039**: Approval records MUST distinguish package-level approval from package-version approval.
- **FR-040**: Manifest validity MUST be separate from approval status.
- **FR-040a**: For sources using `Manual` approval, every newly indexed package version MUST remain pending until explicitly approved, even when the package itself is already approved.
- **FR-041**: Public APIs MUST return only package versions that are listed, approved, and valid.
- **FR-042**: Public APIs MUST expose package listing, package details, package versions, package version details, feature listing, feature details, builder catalog aggregation, infrastructure provider discovery, and compatibility checking.
- **FR-042a**: Runtime Builder-facing APIs MUST expose source/feed provenance for returned package versions and features without exposing source credentials.
- **FR-042b**: Runtime Builder-facing APIs MUST expose manifest-derived feature dependencies, conflicts, required capabilities, settings metadata, and abstract infrastructure requirements for valid, approved, listed package versions.
- **FR-042c**: Infrastructure provider discovery MUST remain separate from package manifests; manifests declare abstract requirements, while builder infrastructure providers describe concrete sidecar, external-service, or platform-managed fulfillment options.
- **FR-043**: Public package listings MUST support filtering by package ID, tag, feature ID, compatibility target, and listed status where applicable.
- **FR-044**: Public feature listings MUST support filtering by package ID, category, required capability, advanced flag, experimental flag, and compatibility target where applicable.
- **FR-045**: Public compatibility checks MUST accept selected package versions, selected features, requested Elsa version, requested Docker image version, runtime capabilities, and license constraints.
- **FR-046**: First-version compatibility checks MUST verify package version existence, listed state, approval state, manifest validity, and declared Elsa version range inclusion when available.
- **FR-047**: Compatibility results MUST include status, errors, warnings, and per-package or per-feature findings that can be rendered by future UI tooling.
- **FR-047a**: Public package and feature discovery APIs MUST NOT expose validation warnings, but compatibility checks MAY return warnings relevant to the submitted package or feature selection.
- **FR-048**: Admin APIs MUST expose source management, sync triggering, sync run history, package review, approval and rejection, and validation details.
- **FR-049**: Admin APIs MUST show invalid, unapproved, rejected, unlisted, and suspicious package versions.
- **FR-050**: Admin APIs MUST be protected by API key authentication in the initial version.
- **FR-051**: Authentication design MUST allow OpenID Connect to be introduced later without changing the public domain model.
- **FR-052**: Public API responses SHOULD be cacheable and MUST avoid exposing admin-only validation internals unless part of a public compatibility response.
- **FR-053**: Sync operations SHOULD be idempotent for unchanged package sources and package versions.
- **FR-054**: The system MUST avoid concurrent syncs for the same source or package where possible.
- **FR-055**: The system MUST log sync activity, validation outcomes, suspicious changes, approval changes, and admin-triggered operations.
- **FR-056**: All stored timestamps MUST be UTC.
- **FR-057**: The initial persistence model MUST use a relational data model that can move from local storage to a managed relational database without changing the domain model.
- **FR-058**: The system MUST use durable storage as the source of truth and MUST NOT rely on cache-only storage for catalog state.
- **FR-059**: The catalog MUST optimize first for correctness and debuggability over scale.
- **FR-060**: The first implementation MUST remain a modular monolith with a core catalog model, API, persistence adapter, and NuGet packaging adapter.

### API Surface

Public endpoints:

- `GET /api/packages`
- `GET /api/packages/{packageId}`
- `GET /api/packages/{packageId}/versions`
- `GET /api/packages/{packageId}/versions/{version}`
- `GET /api/features`
- `GET /api/features/{featureId}`
- `POST /api/compatibility/check`

Admin endpoints:

- `GET /api/admin/sources`
- `POST /api/admin/sources`
- `PUT /api/admin/sources/{id}`
- `DELETE /api/admin/sources/{id}`
- `POST /api/admin/sync`
- `POST /api/admin/sync/sources/{sourceId}`
- `POST /api/admin/sync/packages/{packageId}`
- `GET /api/admin/sync-runs`
- `GET /api/admin/sync-runs/{id}`
- `GET /api/admin/packages`
- `GET /api/admin/packages/{packageId}`
- `POST /api/admin/packages/{packageId}/approve`
- `POST /api/admin/packages/{packageId}/reject`
- `POST /api/admin/packages/{packageId}/versions/{version}/approve`
- `POST /api/admin/packages/{packageId}/versions/{version}/reject`
- `GET /api/admin/packages/{packageId}/versions/{version}/validation`

### Shared Manifest Contract Design

The `Elsa.Platform.PackageManifests` contract is the canonical JSON model shared by manifest generation, catalog ingestion, future runtime validation, and future Runtime Builder tooling.

Contract principles:

- Manifest schema versioning is independent from the NuGet package version.
- The root manifest carries a `schemaVersion` value.
- DTOs preserve unknown extension data and avoid failing on extension metadata in approved extension locations.
- Runtime infrastructure and catalog persistence concerns are excluded.
- Contract types are suitable for serialization, validation, documentation generation, and compatibility checks.
- Validation results are serializable and can be stored or returned by services.

Recommended root manifest shape:

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

Recommended feature shape:

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

Recommended setting shape:

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

### Manifest Schema and Versioning Strategy

- Schema files are versioned by manifest schema version, for example `schemas/elsa-package-manifest.v1.json`.
- Minor-compatible additions must be additive and optional by default.
- Required-field additions require a new schema version.
- Field removals are handled through deprecation first, then removal in a later schema version.
- Deprecated fields remain documented with replacement guidance and validation warnings before becoming errors.
- Extension metadata is allowed under explicit `extensions` objects and should use namespaced keys such as `vendor.featureName`.
- Unknown top-level fields outside approved extension locations should produce validation warnings or errors according to the active schema.
- The catalog stores the raw manifest exactly as extracted so future schema migration or revalidation can be performed.
- Schema migration is a separate future operation; v1 ingestion validates and stores the submitted schema rather than rewriting manifests.
- Compatibility for unsupported future schema versions is conservative: preserve raw JSON, mark unsupported, and hide from public APIs until supported.

### Architecture and Solution Structure

The catalog is an ASP.NET Core modular monolith using an onion-style layering model. `Elsa.Platform.PackageCatalog.Core` is the inner catalog model and workflow layer. API, persistence, and NuGet packaging concerns sit outside the core so the first version remains simple while preserving clear boundaries for future growth.

Suggested solution projects:

- **Elsa.Platform.PackageManifests**: Shared manifest DTOs, schema version constants, JSON serialization settings, validation abstractions and helpers, compatibility models, extension data support, validation result contracts, and embedded JSON Schema resources.
- **Elsa.Platform.PackageCatalog.Api**: Public and admin REST endpoints, API key authentication, request and response contracts, error responses, and API documentation.
- **Elsa.Platform.PackageCatalog.Core**: Catalog entities, value objects, enums, manifest projections, approval rules, immutable package-version behavior, sync status concepts, and small use-case services when they earn their place outside the API layer.
- **Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore**: SQLite-backed persistence, relational mappings, migrations, query projections, and database-provider-neutral model design for later PostgreSQL support.
- **Elsa.Platform.PackageCatalog.Sources.NuGet**: NuGet feed querying, package version discovery, package download, manifest archive inspection, NuGet metadata extraction, and NuGet-specific error translation.

Initial technical constraints:

- Use ASP.NET Core for the service host and REST APIs.
- Use SQLite as the first persistent database.
- Use Entity Framework Core for persistence.
- Use a background worker for periodic synchronization.
- Use NuGet.Protocol or suitable NuGet APIs for feed queries and package downloads.
- Use JSON Schema validation for package manifests.
- Design the domain model so PostgreSQL can be introduced later without changing domain concepts.

### Domain Model

- **Package Source**: A configured source to scan, usually a NuGet feed, with include and exclude selection rules.
- **Package**: A logical NuGet package ID discovered from or configured for a source.
- **Package Version**: A specific NuGet package version plus extracted manifest, validation state, approval state, and listing state.
- **Manifest**: The raw `elsa-package.json` content and parsed contract model extracted from a package version.
- **Feature**: A feature declared by a manifest.
- **Feature Setting**: A configurable setting declared by a feature.
- **Sync Run**: One execution of a scheduled or manual sync job.
- **Sync Run Item**: One package, version, or source-level unit of work inside a sync run.
- **Validation Result**: Stored outcome of manifest validation with errors and warnings.
- **Approval State**: Trust decision that determines whether valid package versions may appear in the public catalog.
- **Suspicious Manifest Change**: A detected manifest hash mismatch for an already indexed package ID and version.

### Key Entities *(include if feature involves data)*

- **PackageSource**: Configured scan source with name, type, URL, enabled state, include and exclude patterns, approval policy, and audit timestamps.
- **Package**: Source-scoped package identity with approval and listing metadata plus the latest public version.
- **PackageVersion**: Immutable version record with version, raw manifest, hash, NuGet metadata snapshot, publication and indexing timestamps, validation status, approval status, listed state, and suspicious-change state.
- **ManifestValidationResult**: Validation status, errors, warnings, schema version, validated timestamp, and raw validation details.
- **FeatureRecord**: Indexed feature identity and summary metadata derived from a package version manifest for query optimization.
- **FeatureSettingRecord**: Indexed setting metadata derived from a feature manifest for query optimization.
- **ApprovalRecord**: Admin decision for package-level or version-level approval, rejection, notes, actor, and timestamp.
- **SyncRun**: Execution record with trigger, status, timestamps, summary counters, and top-level error.
- **SyncRunItem**: Itemized sync outcome with source, package, version, operation status, error, and warning details.

### Data Model

Required tables or equivalent persisted collections:

- **PackageSources**
  - `Id`
  - `Name`
  - `Type`, initially `NuGetFeed`
  - `Url`
  - `Enabled`
  - `IncludePatterns`
  - `ExcludePatterns`
  - `ApprovalPolicy`, either `AutoApprove` or `Manual`
  - `LastSyncedAt`
  - `CreatedAt`
  - `UpdatedAt`

- **Packages**
  - `Id`
  - `PackageId`
  - `SourceId`
  - `Approved`
  - `Listed`
  - `LatestVersion`
  - `CreatedAt`
  - `UpdatedAt`

- **PackageVersions**
  - `Id`
  - `PackageId`
  - `Version`
  - `ManifestJson`
  - `ManifestHash`
  - `NuspecJson`
  - `PublishedAt`
  - `IndexedAt`
  - `ValidationStatus`
  - `ValidationErrors`
  - `ApprovalStatus`
  - `IsListed`
  - `SuspiciousChangeDetected`

- **ManifestValidationResults**
  - `Id`
  - `PackageVersionId`
  - `SchemaVersion`
  - `Status`
  - `Errors`
  - `Warnings`
  - `ValidatedAt`

- **SyncRuns**
  - `Id`
  - `Trigger`, one of `Scheduled`, `ManualAll`, `ManualSource`, `ManualPackage`
  - `Status`, one of `Running`, `Completed`, `Failed`, `CompletedWithErrors`
  - `StartedAt`
  - `CompletedAt`
  - `Error`
  - `SummaryCounters`

- **SyncRunItems**
  - `Id`
  - `SyncRunId`
  - `SourceId`
  - `PackageId`
  - `Version`
  - `Status`
  - `Message`
  - `StartedAt`
  - `CompletedAt`

- **ApprovalRecords**
  - `Id`
  - `TargetType`, either package or package version
  - `TargetId`
  - `Status`, one of pending, approved, rejected
  - `Reason`
  - `Actor`
  - `CreatedAt`

### Sync Behavior

- Scheduled sync scans enabled sources only.
- Manual sync can target all sources, one source, or one package ID.
- Include and exclude patterns determine eligible package IDs using case-insensitive glob syntax; exclude patterns take precedence.
- Sync discovers package versions and skips versions already indexed unless forced reindex is requested.
- Downloaded packages are inspected as package archives only.
- Manifests are extracted from the root `elsa-package.json` path, with `build/elsa-package.json` as a fallback.
- Sync stores raw manifest JSON, hash, validation result, NuGet metadata snapshot when available, and item-level outcome.
- Sync applies source approval policy to new package and version records.
- Sync records summary counters for discovered, downloaded, indexed, unchanged, skipped, invalid, failed, approved, pending approval, and suspicious items.
- Sync attempts to prevent overlapping work for the same source or package and records skipped concurrency attempts.
- Sync failures are isolated to the smallest possible item and surfaced through admin sync run details.

### Manifest Handling

- The canonical manifest file path is `/elsa-package.json` at the NuGet package root.
- The fallback manifest path is `/build/elsa-package.json`.
- The root manifest wins if both canonical and fallback manifests are present.
- The raw manifest JSON is stored unchanged.
- A manifest hash is computed from normalized bytes suitable for detecting content changes.
- The manifest package ID and version must match the NuGet package ID and version.
- Package versions are immutable after first successful indexing.
- Changed manifest content for an existing package ID and version is suspicious and requires admin visibility.
- Invalid manifests remain stored for diagnostics but cannot appear in public APIs.

### Validation Behavior

- Validation runs against the JSON Schema matching the manifest `schemaVersion`.
- Validation supports extension metadata in approved extension objects.
- Unsupported schema versions are stored as validation failures.
- Malformed JSON, missing required fields, identity mismatch, invalid version ranges, invalid dependency declarations, invalid conflicts, and invalid setting schemas are validation failures.
- Extracted manifests larger than 1 MB are validation failures in the first version.
- Unknown extension metadata under approved locations is preserved.
- Validation failures are persisted and available through admin validation endpoints.
- Validation warnings do not by themselves hide a package version if the manifest is otherwise valid, approved, and listed.
- Public package and feature discovery APIs hide validation warnings; public compatibility checks may return warnings that are relevant to the submitted selection.
- Validation results are stable enough to be compared across sync runs.

### Approval Workflow

- Source approval policy determines the initial approval state for newly indexed packages and versions.
- `AutoApprove` marks technically valid new entries as approved unless a package or version is already rejected.
- `Manual` leaves every newly indexed package version pending admin approval, even when the package itself is already approved.
- Package approval and version approval are separate decisions.
- Rejection hides the rejected target from public APIs and records a reason when provided.
- Approval does not override invalid validation status.
- Admin APIs expose all pending, approved, rejected, invalid, unlisted, and suspicious records.
- Suspicious manifest changes require explicit admin review and are not automatically public.

### Error Handling

- Public APIs return stable problem details for missing packages, missing versions, invalid compatibility requests, and unsupported filters.
- Public 404 responses do not reveal hidden, rejected, or invalid versions as public catalog entries.
- Admin APIs distinguish not found, unauthorized, invalid request, sync already running, validation failure, and source connectivity errors.
- Sync item errors include enough context to identify source, package ID, version, operation, and failure reason.
- Validation errors include field paths and rule identifiers where possible.

### Security Considerations

- Admin endpoints are protected by API key authentication for the initial version.
- API keys are never logged.
- The design must allow OpenID Connect to be added later for admin APIs.
- Package source credentials are out of scope for the first version; configured sources that require credentials must not leak attempted credential prompts or secrets into logs.
- The catalog does not execute package code.
- The catalog does not load arbitrary package assemblies.
- Package archives are treated as untrusted input.
- Manifest JSON size limits prevent oversized payload abuse.
- Source URLs and package metadata are validated before use.
- Approval and rejection operations are auditable.
- Public endpoints never expose admin API keys, source secrets, or unapproved package diagnostics.

### Observability and Logging

- Log every sync run start, completion, failure, and summary.
- Log source-level and package-level sync failures with source and package identifiers.
- Log manifest validation status and suspicious manifest hash mismatches.
- Log approval and rejection decisions with actor and target.
- Expose sync run history through admin APIs for debugging.
- Track counters for discovered packages, indexed versions, invalid manifests, skipped versions, failures, and suspicious changes.
- Use UTC timestamps consistently in logs and persisted records.

### Testing Strategy

- Contract tests verify manifest DTO serialization, deserialization, extension data preservation, schema constants, and validation result shape.
- Schema tests validate representative valid and invalid manifests for each supported schema version.
- Sync tests use controlled package sources and package archives covering valid, invalid, missing-manifest, duplicate, and suspicious-change cases.
- Public API tests verify hidden records are never returned unless listed, approved, and valid.
- Admin API tests verify source management, sync triggering, sync history, validation inspection, approval, and rejection.
- Compatibility tests verify success, warning, and error outcomes for selected package sets.
- Persistence tests verify immutable package versions, UTC timestamps, idempotent sync, and relational model constraints.
- Security tests verify admin API key enforcement and that public APIs do not leak admin-only state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A consumer can serialize, deserialize, and validate a representative manifest with extension metadata while preserving all unknown extension data.
- **SC-002**: An administrator can configure a package source with include and exclude patterns and trigger a sync in under 5 minutes of operator time.
- **SC-003**: A scheduled or manual sync of a controlled feed with at least 20 matching package versions records valid, invalid, skipped, and failed items without stopping on the first failure.
- **SC-004**: Public package and feature queries return zero invalid, unapproved, rejected, or unlisted package versions in test data containing all of those states.
- **SC-005**: Re-indexing an unchanged package source creates no duplicate package version records.
- **SC-006**: A changed manifest for an already indexed package ID and version is detected and visible to administrators every time it appears.
- **SC-007**: Admin sync run history shows run status, timestamps, summary counters, and item-level errors for the latest completed run.
- **SC-008**: Compatibility checks return actionable pass, warning, or error findings for every selected package in a request.
- **SC-009**: The stored domain model can represent package sources, packages, versions, manifests, validation results, approval records, and sync history without depending on a specific relational database vendor.
- **SC-010**: No catalog operation executes package code or loads package assemblies during validation or synchronization.
- **SC-011**: A controlled package with an `elsa-package.json` larger than 1 MB is stored with a validation failure and never appears in public package or feature APIs.

## Acceptance Criteria

1. A stable shared manifest contract package exists.
2. Manifest DTOs support versioning and extension metadata.
3. An admin can configure a NuGet package source with include and exclude patterns.
4. A scheduled sync can discover matching packages and versions.
5. A manual sync can be triggered for all sources, one source, or one package.
6. The service extracts `elsa-package.json` from NuGet packages.
7. The service validates manifests and stores validation results.
8. The service stores package versions immutably and detects changed manifests for the same package version.
9. Public APIs expose only approved, listed, valid package versions.
10. Admin APIs expose invalid or unapproved packages and validation errors.
11. Sync run history is available.
12. The API shape is suitable for a future UI that lets users select packages and configure features.

## Assumptions

- The initial application is implemented as a modular monolith named Elsa Package Catalog.
- The initial shared contract package is named `Elsa.Platform.PackageManifests`.
- The initial service uses REST APIs and separates public APIs from admin APIs.
- The initial durable store is SQLite with a relational model designed so PostgreSQL can be introduced later.
- Persistence uses an abstraction that keeps the domain model independent from the database provider.
- NuGet package discovery and download use suitable NuGet client APIs.
- JSON Schema validation is the primary structural manifest validation mechanism.
- API key authentication is sufficient for the first admin API version.
- Package source credentials are out of scope for the first version; only unauthenticated NuGet feeds are supported.
- The manifest generator is future work and will emit `elsa-package.json`; this feature only defines the contract and consumes generated manifests.
- Full dependency resolution and license validation are future work; v1 stores declarations and performs basic checks only.
