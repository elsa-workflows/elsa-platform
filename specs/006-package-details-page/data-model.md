# Data Model: Package Details Page

## Package

Existing catalog package identity.

Fields relevant to details:

- `packageId`: Canonical indexed package identity displayed by the UI.
- `source`: Package source summary associated with the package.
- `latestVersion`: Latest indexed version string when known.
- `versions`: All indexed package versions visible to administrators.
- `listed`: Package-level listing state.
- `approved`: Legacy package-level approval flag used only as historical context.
- `createdAt`: Package creation timestamp.
- `updatedAt`: Last package update timestamp.

Rules:

- Route matching resolves package IDs case-insensitively.
- Responses display canonical indexed casing.
- Package identity rows do not perform trust-changing actions; actions target a
  selected package version.
- A package may exist with no indexed versions and must still produce a useful
  admin empty state.

## Package Source Summary

Administrator-facing source identity for the package.

Fields:

- `id`: Source identity.
- `name`: Source display name.
- `url`: Sanitized source URL or feed label appropriate for admin display.
- `enabled`: Whether scheduled sync should include the source.
- `status`: Health state when known.
- `lastSyncedAt`: Last sync attempt timestamp when known.
- `lastSuccessfulSyncAt`: Last successful sync timestamp when known.

Rules:

- Source data is read-only on the package details page.
- Disabled or unhealthy source state contributes to visibility explanation when
  it affects whether a version should be trusted or discoverable.

## Package Version

Existing versioned package record selected by the details page.

Fields relevant to details:

- `version`: Package version string.
- `approvalStatus`: Version approval status.
- `validationStatus`: Version validation status.
- `isListed`: Version-level listing state.
- `suspiciousChangeDetected`: Whether an immutable version changed manifest
  content after indexing.
- `manifestHash`: Stored manifest hash for the indexed version.
- `suspiciousManifestHash`: Observed changed hash when suspicious.
- `schemaVersion`: Manifest schema version.
- `publishedAt`: Package publication timestamp when available.
- `indexedAt`: Catalog indexing timestamp.
- `manifestJson`: Read-only raw manifest content.
- `features`: Indexed feature records.
- `compatibility`: Compatibility metadata for the selected version when indexed.
- `versionStateToken`: Opaque freshness marker derived from the selected
  version's review-relevant state.

Rules:

- The latest indexed version is selected by default when the route does not
  specify a version.
- All version-scoped sections and actions derive from the selected version.
- Version routes and version-specific links preserve the selected version.
- Stale trust-changing actions compare the loaded `versionStateToken` with the
  current version state and are blocked until the administrator refreshes and
  reviews the current state.

## Visibility Reason

Normalized explanation of why the selected version is publicly visible or hidden.

Fields:

- `code`: Stable reason identifier.
- `severity`: Informational, warning, or blocking display severity.
- `category`: Trust decision, validation, listing, source, manifest, or ingestion.
- `message`: Human-readable explanation.
- `blocksPublicVisibility`: Whether the reason prevents public visibility.

Rules:

- A visible version includes positive reasons that explain why it is visible.
- A hidden version includes every known blocking reason.
- Reasons distinguish package approval, version approval, rejection, validation,
  listing, suspicious manifest changes, source state, missing manifest data, and
  ingestion failures.

## Validation Finding

Normalized validation diagnostic for one selected version.

Fields:

- `severity`: Error, warning, or informational severity.
- `code`: Rule identifier when available.
- `message`: Diagnostic message.
- `path`: Manifest field path when available.
- `blocksPublicVisibility`: Whether the finding blocks public listing.
- `validatedAt`: Validation timestamp.
- `validatorVersion`: Validator version when available.

Rules:

- Historic JSON result payloads are normalized to finding records.
- Findings without code or path remain visible and searchable.
- Validation load failure affects only the validation section when package
  details otherwise load.

## Feature

Existing indexed feature record on a package version.

Fields:

- `featureId`: Feature identity.
- `typeName`: Technical type name when available.
- `displayName`: User-facing feature name.
- `description`: Feature description.
- `category`: Feature category.
- `requiredCapabilities`: Required runtime capabilities.
- `dependencies`: Feature or package dependencies.
- `conflicts`: Feature or package conflicts.
- `infrastructure`: Infrastructure or provider requirements.
- `advanced`: Advanced feature flag.
- `experimental`: Experimental feature flag.
- `extensions`: Additional extension metadata.
- `settings`: Feature setting records.

Rules:

- Feature list supports in-page search and filtering.
- Dependencies, conflicts, and related infrastructure metadata are visible from
  the selected version.
- Empty feature lists show an explicit no-indexed-features state.

## Compatibility Metadata

Indexed compatibility information for the selected package version.

Fields:

- `targetFrameworks`: Supported target framework or runtime ranges when known.
- `elsaVersionRange`: Supported Elsa version range when known.
- `requiredCapabilities`: Runtime capabilities required by the package version.
- `notes`: Human-readable compatibility notes.
- `unsupportedCombinations`: Known unsupported target, runtime, package, feature,
  or capability combinations.

Rules:

- Compatibility metadata is read-only.
- Missing compatibility metadata produces a scoped empty state.
- Compatibility metadata is never inferred from package implementation code.
- Compatibility filtering supports target, runtime, capability, and unsupported
  combination terms when those values are present.

## Feature Setting

Existing indexed setting record attached to a feature.

Fields:

- `name`: Setting identity.
- `displayName`: Display label.
- `description`: Setting help text.
- `category`: Setting category.
- `jsonType`: JSON value type.
- `clrType`: CLR type when available.
- `required`: Whether a value is required.
- `defaultValueJson`: Default value presence and content when available.
- `validationJson`: Validation hints.
- `secret`: Whether the setting represents secret material.
- `restartRequired`: Whether changes require restart in future runtime tooling.
- `environmentVariable`: Suggested environment variable name when available.
- `uiJson`: UI hints.
- `extensionsJson`: Extension metadata.

Rules:

- Settings are read-only in the details page.
- Secret settings must not imply that secret values are stored or displayed.
- Setting lists support in-page search and filtering.

## Manifest

Stored manifest identity and content for a package version.

Fields:

- `schemaVersion`: Manifest schema version.
- `manifestHash`: Stored hash for the indexed manifest.
- `suspiciousManifestHash`: Observed changed hash when suspicious.
- `manifestJson`: Read-only manifest JSON.
- `available`: Whether manifest content exists and is usable for display.

Rules:

- Manifest content is never editable from the details page.
- Missing or malformed manifest content produces a scoped unavailable state.
- Large manifest content supports in-page search and navigation.

## Version Action

Administrator operation against one package version.

Fields:

- `action`: Approve, reject, revalidate, resync, or recompute metadata.
- `packageId`: Canonical package ID.
- `version`: Selected version.
- `reason`: Optional for approval and required for rejection.
- `available`: Whether the current system supports the action.
- `requiresFreshState`: Whether stale state blocks execution.
- `expectedStateToken`: The version state token reviewed by the administrator
  before submitting a trust-changing action.

Rules:

- Approval and rejection identify package ID and version before submission.
- Rejection requires a non-empty reason.
- Unsupported optional actions are omitted or disabled with an explanation.
- Trust-changing actions include or compare the reviewed state token so stale
  decisions are rejected before changing approval state.
- Action success refreshes affected version state.
- Action failure preserves package and version context.
