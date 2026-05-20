# Research: Package Details Page

## Decision: Extend the existing admin package details endpoint

**Rationale**: The dashboard already uses `GET /api/admin/packages/{packageId}`
for package details and list rows link to `/admin/packages/{packageId}`. Extending
that administrator-only projection keeps package details in one discoverable
contract and avoids introducing a second read model for the same route.

**Alternatives considered**:

- **Create many section-specific endpoints**: Rejected for the first version
  because the page needs consistent version-scoped state across summary,
  visibility, features, validation, and manifest sections.
- **Use public package endpoints for most data**: Rejected because public
  endpoints intentionally hide pending, rejected, invalid, suspicious, and
  unlisted versions.
- **Expose raw persistence models directly**: Rejected because admin UI contracts
  should stay stable and separate from EF Core shape.

## Decision: Resolve package IDs case-insensitively and return canonical casing

**Rationale**: Package ID links should be forgiving while the page displays the
canonical indexed package ID. This matches NuGet-style package identity behavior
and supports shared links with casing differences.

**Alternatives considered**:

- **Case-sensitive route matching**: Rejected because it creates needless
  not-found states for otherwise valid package links.
- **Display route casing**: Rejected because administrators should see the
  catalog's canonical indexed identity, especially for approval and audit
  decisions.

## Decision: Select the latest indexed version by default

**Rationale**: Administrators expect package details opened from the package list
to describe the current indexed state. The latest indexed version may be pending,
invalid, unlisted, or suspicious, which is exactly what administrators need to
inspect.

**Alternatives considered**:

- **Latest publicly visible version**: Rejected because it can hide the version
  most likely to need administrative attention.
- **Most urgent review state**: Rejected because it makes initial selection
  harder to predict and complicates shared troubleshooting.
- **Require version in every route**: Rejected because package-level links are
  already part of the dashboard flow.

## Decision: Use version and section deep links

**Rationale**: Troubleshooting often happens by sharing a direct link to a
specific version and diagnostic area. Supporting major sections keeps links
useful without making every subpanel a separate workflow.

**Alternatives considered**:

- **Version-only links**: Rejected because validation and manifest reviews would
  still require manual navigation after opening a shared link.
- **Package-only links**: Rejected because package-version decisions need a
  stable selected version.
- **Deep link every row and finding**: Deferred because it adds routing detail
  beyond the first useful operational slice.

## Decision: Compute visibility reasons for the selected version in the admin contract

**Rationale**: The administrator needs one authoritative explanation of why a
version is visible or hidden. Computing normalized visibility reasons near the
admin package projection avoids duplicating the same rules across UI tests and
future admin consumers.

**Alternatives considered**:

- **Compute all visibility reasons only in the UI**: Rejected because it risks
  diverging from backend visibility policy and makes API tests less valuable.
- **Only show raw statuses**: Rejected because the spec requires explicit,
  grouped visibility explanations.

## Decision: Normalize validation result JSON into display findings

**Rationale**: Existing validation result records may store JSON payloads for
errors and warnings. The admin page needs sortable/searchable finding records
with severity, code, message, path, and blocking impact. Normalization can happen
in the admin response so the UI does not need to understand every historic JSON
variant.

**Alternatives considered**:

- **Render raw validation JSON only**: Rejected because it does not meet the
  diagnostic usability requirement.
- **Create a new validation table**: Rejected because no new durable entity is
  needed for this display feature.

## Decision: Use existing feature and setting records for feature inspection

**Rationale**: Feature and setting projections already exist as indexed data on
package versions. The details page should display those records, including JSON
surfaces for dependencies, conflicts, infrastructure, validation, UI hints, and
extensions when available.

**Alternatives considered**:

- **Re-parse manifest JSON on every details request**: Rejected because indexed
  feature records already represent the catalog projection and avoid repeated
  parsing work.
- **Hide raw JSON-backed surfaces**: Rejected because dependencies, conflicts,
  and compatibility data are key troubleshooting signals for this page.

## Decision: Block stale trust-changing actions until refresh

**Rationale**: Approval and rejection decisions should apply to the version state
the administrator actually reviewed. If the selected version changed after page
load, the page should explain the conflict and require refresh before retry.

**Alternatives considered**:

- **Retry automatically after refresh**: Rejected because the administrator may
  not have reviewed the newer state.
- **Allow the action and show newer state afterward**: Rejected because it can
  apply a trust decision to stale information.

## Decision: Keep large-section search and filtering in the page

**Rationale**: The scale requirement is about administrator inspection, not
server-side data discovery. In-page search/filtering over the loaded details
keeps the first version simple and makes validation findings, features, settings,
dependencies, conflicts, and manifest content practical to inspect.

**Alternatives considered**:

- **Browser text search only**: Rejected because it cannot filter table-like
  sections by status, severity, field, or relationship.
- **Server-side pagination for every section**: Deferred until real package
  sizes exceed the current stated targets.
