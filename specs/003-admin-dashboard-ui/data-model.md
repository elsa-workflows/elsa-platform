# Data Model: Elsa Control Package Catalog Console UI

The dashboard does not own durable domain data. These models describe the UI's
view models and the admin API data it consumes or mutates.

## Package Source

Represents a configured package feed shown on Sources.

Fields:

- `id`: stable source identifier.
- `name`: administrator-facing source name.
- `type`: source type, initially `NuGetFeed`.
- `url`: feed URL.
- `enabled`: whether scheduled sync should process the source.
- `includePatterns`: case-insensitive glob patterns.
- `excludePatterns`: case-insensitive glob patterns; excludes take precedence.
- `approvalPolicy`: `AutoApprove` or `Manual`.
- `lastSyncedAt`: most recent sync timestamp, if any.
- `lastSuccessfulSyncAt`: guaranteed source health field when available from the
  admin API; otherwise equivalent to successful sync history.
- `status`: guaranteed source health field such as `Healthy`, `Warning`, or
  `Error`.
- `isSyncing`: transient admin API state indicating the source is actively
  being processed by a manual or scheduled sync in the current API host.
- `packageCount`: count shown on source list when available.
- `createdAt`, `updatedAt`: audit timestamps.
- `softDeletedAt`: present when a source has been removed from active source
  management.

Relationships:

- Has many Package records.
- Has many Sync Run Items through source ID.

Validation rules:

- Name and URL are required.
- Include patterns must contain at least one non-empty pattern.
- Exclude patterns are optional.
- Pattern matching is case-insensitive glob matching.
- Soft-deleted sources are hidden from the default active source list.

State transitions:

- Active enabled -> Active disabled.
- Active disabled -> Active enabled.
- Active enabled/disabled -> Soft-deleted.
- Soft-deleted sources are not hard-deleted through the MVP UI.

## Source Health

Operational source status shown in the source list and detail view.

Fields:

- `sourceId`: source identifier.
- `status`: guaranteed summary status.
- `lastSuccessfulSyncAt`: guaranteed successful sync timestamp when known.
- `recentSyncRuns`: recent run summaries used for diagnostics.
- `isSyncing`: current in-process sync activity, used for an in-progress UI
  label without changing durable source health.
- `validationFailureCount`: derived from recent sync run items when available.
- `authenticationFailures`: derived from recent sync run items or diagnostics.
- `connectivityIssues`: derived from recent sync run items or diagnostics.

Rules:

- Only `status` and `lastSuccessfulSyncAt` are treated as guaranteed fields.
- Derived diagnostics must be labeled as recent-run evidence, not permanent
  source truth.

## Pattern Test Case

Transient UI-only model used by the include/exclude pattern tester.

Fields:

- `samplePackageId`: administrator-entered package ID.
- `matchedIncludePatterns`: include patterns that matched.
- `matchedExcludePatterns`: exclude patterns that matched.
- `result`: `Included` or `Excluded`.
- `reason`: short explanation such as `Matched include` or `Excluded wins`.

Rules:

- Exclude matches override include matches.
- Empty sample rows are ignored.

## Package

Aggregate package identity displayed in the Packages list and details header.

Fields:

- `packageId`: NuGet package ID.
- `sourceId`: owning source ID.
- `latestVersion`: latest indexed version known to admin APIs.
- `aggregateValidationStatus`: summary status for list scanning.
- `aggregateListingState`: summary public visibility indicator.
- `featuresCount`: count for latest or selected version.
- `updatedAt`: last indexed or approval-change timestamp when available.
- `versions`: list of Package Version records.

Relationships:

- Belongs to one Package Source.
- Has many Package Versions.

Rules:

- Package identity approval controls are not exposed in the MVP.
- List rows may summarize package state, but trust-changing actions target
  package versions.

## Package Version

Specific immutable package version displayed, approved, rejected, re-synced, and
diagnosed.

Fields:

- `packageId`: parent package ID.
- `version`: package version.
- `sourceId`: owning source ID.
- `publishedAt`: package publish timestamp when available.
- `indexedAt`: catalog indexing timestamp.
- `manifestHash`: stored manifest hash.
- `suspiciousManifestHash`: observed conflicting hash when suspicious.
- `approvalStatus`: `Pending`, `Approved`, or `Rejected`.
- `rejectionReason`: required when rejected by the dashboard.
- `validationStatus`: `NotValidated`, `Valid`, `Invalid`,
  `UnsupportedSchema`, or `Suspicious`.
- `isListed`: listing state.
- `suspiciousChangeDetected`: immutable-version warning flag.
- `schemaVersion`: manifest schema version.
- `features`: feature metadata records when available.
- `manifestJson`: raw manifest JSON when exposed by admin API.

Visibility explanation inputs:

- Not approved or rejected.
- Validation is not valid.
- Unlisted.
- Suspicious manifest change.

State transitions:

- Pending -> Approved.
- Pending -> Rejected with reason.
- Approved -> Rejected with reason.
- Rejected -> Approved.
- Any state may become hidden if validation, listing, or suspicious state blocks
  public visibility.

## Feature Metadata

Manifest-derived feature information in Package Details.

Fields:

- `featureId`.
- `displayName`.
- `description`.
- `settingsCount`.
- `compatibility`.
- `dependencies`.
- `conflicts`.
- `advanced`.
- `experimental`.

Rules:

- Displayed for inspection only.
- No feature enablement or runtime-builder selection is performed in the admin
  dashboard.

## Validation Result

Persisted validation diagnostics for a package version.

Fields:

- `id`.
- `schemaVersion`.
- `status`.
- `errors`.
- `warnings`.
- `validatedAt`.
- `validatorVersion`.

Validation Finding fields:

- `severity`.
- `code` or `ruleId`.
- `message`.
- `fieldPath` or `path`.

Rules:

- Unknown issue codes still render.
- Malformed or JSON-encoded error payloads are shown as available diagnostics
  without breaking the page.

## Manifest View

Raw and formatted manifest inspection state.

Fields:

- `packageId`.
- `version`.
- `schemaVersion`.
- `manifestHash`.
- `rawJson`.
- `formattedJson`.
- `formattingStatus`: `Formatted`, `RawOnly`, or `Unavailable`.

Rules:

- Read-only.
- Never executes content.
- Malformed JSON falls back to raw inspection.

## Sync Run

Synchronization operation shown on Sync Runs.

Fields:

- `id`.
- `trigger`: `Scheduled`, `ManualAll`, `ManualSource`, or `ManualPackage`.
- `status`: `Running`, `Completed`, `Failed`, `CompletedWithErrors`, or
  `Canceled`.
- `startedAt`.
- `completedAt`.
- `duration`.
- `error`.
- `summaryCounters`.
- `itemCount`.
- `sources`: compact source references associated with the run.
- `packagesScanned`.
- `packagesUpdated`.
- `failures`.
- `items`.

Relationships:

- Has many Sync Run Items.
- Source references are derived from sync items for list/detail diagnostics.
- Items may reference source, package ID, and version.
- Only `Running` runs can be canceled; cancellation is best-effort and becomes
  visible as `Canceled` after in-flight sync work observes the cancellation
  token.

## Sync Run Item

Per-source or per-package sync diagnostic row.

Fields:

- `id`.
- `sourceId`.
- `packageId`.
- `version`.
- `status`.
- `message`.
- `error`.
- `startedAt`.
- `completedAt`.

Rules:

- Failed items do not imply the whole run failed.
- Items with package records link to Package Details.

## Admin Operation

Transient UI state for mutations.

Fields:

- `operation`: create source, update source, disable source, soft-delete source,
  sync source, approve version, reject version, re-sync version, revalidate
  version, recompute metadata.
- `targetIds`.
- `status`: idle, pending, succeeded, failed, partially failed.
- `error`.
- `itemResults`.

Rules:

- Duplicate submissions are blocked while pending.
- Destructive or trust-changing operations require confirmation.
- Reject operations require a non-empty reason.
