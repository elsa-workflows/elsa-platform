# Feature Specification: Package Details Page

**Feature Branch**: `006-package-details-page`

**Created**: 2026-05-17

**Status**: Draft

**Input**: User description: "Specify the functionality of the package details page in the admin dashboard so administrators can inspect package identity, versions, approval and validation state, visibility reasons, features, dependencies, compatibility, raw manifest content, and operational actions from the placeholder Package Details screen."

## Overview

The Package Details page turns the current placeholder admin dashboard route into
an operational inspection view for one catalog package. Administrators use this
page to understand what was indexed, which package versions are available, why a
version is or is not publicly visible, what validation found, what features and
settings the package contributes, and which version-scoped review actions are
available.

The page is a read-first workspace. It should make the selected package version,
approval state, validation state, listing state, source, manifest identity,
feature surface, dependency relationships, compatibility signals, and raw
manifest content easy to inspect without requiring users to leave the dashboard.
Actions such as approving, rejecting, revalidating, or resyncing are deliberate,
version-scoped, and shown only when they are available for the selected version.

The first version is not a marketplace package page, manifest editor, dependency
graph visualizer, or analytics product. Its job is to help technical
administrators answer, quickly and confidently, "What is this package, what does
this version contain, and what prevents it from being visible?"

## Clarifications

### Session 2026-05-17

- Q: Which package version should be selected by default when a package has multiple indexed versions? -> A: Latest indexed version.
- Q: How should route package ID casing be handled? -> A: Resolve case-insensitively and display canonical indexed casing.
- Q: What level of deep linking should the details page support? -> A: Direct links to package version and major section.
- Q: How should trust-changing actions behave when the package version changed after the page loaded? -> A: Block the action and require refresh before retry.
- Q: How should administrators inspect large validation, feature, setting, dependency, and manifest sections? -> A: Provide in-page search and filtering.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect Package Summary (Priority: P1)

An administrator can open a package details page from a package link or direct
URL and immediately understand the package identity, selected version, source,
status, and public visibility.

**Why this priority**: The page must first answer whether the administrator is
looking at the right package and what the current operational state is.

**Independent Test**: Open a known package details route for a package with more
than one version and verify the page shows the package summary, selected
version, source, timestamps, status badges, and visibility explanation without
requiring any secondary navigation.

**Acceptance Scenarios**:

1. **Given** an indexed package exists, **When** an administrator opens its
   details page, **Then** the page shows the package ID, selected version,
   source name, latest known version, published date when available, indexed
   date, and last updated date.
2. **Given** the package has multiple indexed versions, **When** the page opens,
   **Then** the latest indexed version is selected by default and all
   version-specific summary fields clearly correspond to that selected version.
3. **Given** the selected version is publicly visible, **When** the summary is
   displayed, **Then** the page states that the version is visible and shows the
   positive reasons supporting that state.
4. **Given** the selected version is not publicly visible, **When** the summary
   is displayed, **Then** the page lists every known blocking reason, such as
   pending approval, rejection, validation failure, unlisted package, missing
   manifest, source disabled, or suspicious manifest change.
5. **Given** the package route references a package that does not exist or is no
   longer accessible, **When** the page loads, **Then** the administrator sees a
   clear not-found state with a path back to the packages list.
6. **Given** the package ID in the route uses different casing than the indexed
   package ID, **When** the page loads, **Then** the package is resolved
   case-insensitively and displayed using its canonical indexed casing.

---

### User Story 2 - Compare and Select Versions (Priority: P1)

An administrator can inspect the available versions of a package, switch the
selected version, and see how approval, validation, listing, manifest, and
visibility state differ per version.

**Why this priority**: Catalog review and public visibility decisions operate on
package versions, so version selection must be explicit and trustworthy.

**Independent Test**: Seed a package with approved, pending, rejected, invalid,
unlisted, and suspicious versions; switch between them and confirm every
version-scoped section updates consistently.

**Acceptance Scenarios**:

1. **Given** a package has several versions, **When** the administrator views
   the version list, **Then** each version shows approval status, validation
   status, listing state, suspicious state, and indexed timestamp when known.
2. **Given** the administrator selects another version, **When** the selection
   changes, **Then** the summary, visibility reasons, feature list, validation
   findings, manifest metadata, raw manifest, and available actions all update
   to reflect the newly selected version.
3. **Given** a version has incomplete indexed data, **When** it is selected,
   **Then** sections with missing data show a specific empty or unavailable
   state instead of silently reusing data from another version.
4. **Given** a direct URL identifies a specific version, **When** the page
   opens, **Then** that version is selected when it exists and a recoverable
   version-not-found message is shown when it does not.
5. **Given** a direct URL identifies a specific package version and major
   section, **When** the page opens, **Then** that version is selected and the
   requested section is brought into view when both exist.

---

### User Story 3 - Diagnose Validation and Visibility (Priority: P1)

A support engineer can inspect validation findings, suspicious manifest changes,
and visibility blockers for the selected version in enough detail to explain
what must change before the version can be shown publicly.

**Why this priority**: Troubleshooting package publication is one of the main
reasons to open package details.

**Independent Test**: Open details for versions with no findings, warnings,
errors, suspicious hash changes, and multiple visibility blockers; verify each
case is understandable and grouped by severity and impact.

**Acceptance Scenarios**:

1. **Given** validation errors or warnings exist, **When** the administrator
   views the validation section, **Then** each finding shows severity, code or
   rule identifier when available, message, affected field path when available,
   and whether the finding blocks public visibility.
2. **Given** no validation findings exist for the selected version, **When** the
   validation section is viewed, **Then** the page shows a valid state rather
   than an empty table.
3. **Given** a version is marked suspicious because an immutable version's
   manifest changed, **When** package details are viewed, **Then** the page
   highlights the suspicious state and shows the relevant manifest hash evidence
   available to administrators.
4. **Given** multiple blockers apply to a version, **When** visibility reasons
   are shown, **Then** they are grouped so administrators can distinguish trust
   decisions, validation outcomes, listing state, source state, and ingestion
   problems.
5. **Given** validation data cannot be loaded, **When** the page finishes
   loading available package data, **Then** the validation area shows a scoped
   failure state while preserving the rest of the package details page.

---

### User Story 4 - Inspect Features, Settings, Dependencies, and Compatibility (Priority: P2)

An administrator can understand the functional surface contributed by the
selected version, including features, settings, dependency requirements,
conflicts, and compatibility metadata.

**Why this priority**: Package review requires knowing what capabilities a
package contributes and whether those capabilities are compatible with the
catalog's target consumers.

**Independent Test**: Open a package version with several features, settings,
dependencies, conflicts, compatibility ranges, and a package version with none
of those details; verify both rich and empty states are useful.

**Acceptance Scenarios**:

1. **Given** the selected version contains features, **When** the feature section
   is viewed, **Then** each feature shows its display name, technical name when
   available, description when available, category when available, and setting
   count.
2. **Given** a feature has settings, **When** the administrator expands or opens
   that feature's settings, **Then** the setting name, display label, type,
   default value presence, required state, and validation hints are visible when
   available.
3. **Given** the selected version declares dependencies or conflicts, **When**
   those sections are viewed, **Then** the page shows package IDs, version
   ranges or constraints, relationship type, and any compatibility result known
   to the catalog.
4. **Given** compatibility metadata exists, **When** the compatibility section is
   viewed, **Then** administrators can see supported target framework or runtime
   ranges, Elsa version ranges, required runtime capabilities, package-specific
   compatibility notes, and any known unsupported combinations.
5. **Given** the selected version has no indexed features, settings,
   dependencies, conflicts, or compatibility metadata, **When** the relevant
   section is viewed, **Then** the page states that no data was indexed for that
   section.
6. **Given** a package version has a large number of features, settings,
   dependencies, or conflicts, **When** the administrator inspects those
   sections, **Then** in-page search and filtering help narrow the displayed
   items without leaving the details page.

---

### User Story 5 - Review Manifest Content and Version Actions (Priority: P2)

An administrator can inspect manifest metadata and raw manifest content, then
perform available version-scoped operational actions through deliberate
confirmation flows.

**Why this priority**: Raw manifest review and trust-changing actions are needed
for package approval support, but they should not distract from the primary
inspection flow.

**Independent Test**: Open versions with available and missing manifest content,
approve a pending version, reject a version with a reason, and verify unsupported
actions are omitted or clearly unavailable.

**Acceptance Scenarios**:

1. **Given** manifest content exists for the selected version, **When** the
   manifest section is opened, **Then** the page shows schema version, manifest
   hash, manifest size or equivalent cue when available, and formatted manifest
   content in read-only form.
2. **Given** the administrator reviews raw manifest content, **When** the content
   is large, **Then** the page remains navigable and provides a way to search or
   jump within the manifest content.
3. **Given** a version can be approved, **When** the administrator chooses to
   approve it, **Then** the confirmation identifies the package ID and version
   and the page reflects the new approval state after success.
4. **Given** a version can be rejected, **When** the administrator chooses to
   reject it, **Then** the confirmation requires a non-empty rejection reason and
   the page reflects the rejection state after success.
5. **Given** optional actions such as revalidation, resync, or metadata
   recomputation are unavailable for the selected version, **When** actions are
   shown, **Then** unavailable actions are either omitted or shown disabled with
   a clear reason.
6. **Given** an action partially fails or cannot be completed, **When** the
   result is returned, **Then** the page keeps the administrator on the selected
   version and explains what succeeded, what failed, and whether refresh is
   needed.
7. **Given** the selected version changed after the page loaded, **When** the
   administrator submits a trust-changing action, **Then** the action is blocked,
   the page explains that the version state changed, and the administrator must
   refresh before retrying.

### Edge Cases

- The package exists but has no indexed versions.
- The selected version exists but its manifest content is missing or malformed.
- A version has validation findings but no field paths or rule identifiers.
- Visibility state cannot be fully determined because one supporting data source
  is unavailable.
- The administrator loses access while viewing the page or submitting an action.
- A version has a very large number of features, settings, dependencies, or
  validation findings.
- The browser is refreshed or the direct URL is shared while a non-default
  version or major section is selected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The page MUST support opening package details by package ID and,
  when supplied, by a specific package version.
- **FR-002**: The page MUST display a package summary containing package ID,
  selected version, source, latest known version, published date when available,
  indexed date, last updated date, approval status, validation status, listing
  state, suspicious state, and public visibility state.
- **FR-003**: Route package ID matching MUST be case-insensitive, and the page
  MUST display the package ID using canonical indexed casing.
- **FR-004**: The page MUST make the selected version explicit at all times.
- **FR-005**: Administrators MUST be able to switch among all indexed versions
  of the package.
- **FR-006**: When no version is supplied, the page MUST select the latest
  indexed version by default, regardless of public visibility.
- **FR-007**: Version switching MUST update all version-scoped sections and
  actions consistently.
- **FR-008**: The page MUST explain public visibility for the selected version
  using all known applicable reasons.
- **FR-009**: The page MUST distinguish visibility blockers caused by approval,
  rejection, validation, listing state, suspicious manifest changes, missing
  manifest data, source state, and ingestion failures.
- **FR-010**: The page MUST display validation findings for the selected version
  with severity, code or rule identifier when available, message, affected field
  path when available, and blocking impact.
- **FR-011**: The page MUST provide useful valid, empty, unavailable, loading,
  not-found, access-denied, conflict, and unexpected-error states.
- **FR-012**: The page MUST display feature metadata for the selected version,
  including feature identity, display name, description, category, and setting
  count when available.
- **FR-013**: The page MUST let administrators inspect settings for a feature,
  including setting identity, label, value type, required state, default value
  presence, and validation hints when available.
- **FR-014**: The page MUST display dependencies, conflicts, and compatibility
  metadata for the selected version when indexed, including target framework
  ranges, Elsa version ranges, required capabilities, notes, and unsupported
  combinations when those values are available.
- **FR-015**: The page MUST display manifest metadata and read-only raw manifest
  content when available.
- **FR-016**: The raw manifest view MUST support efficient inspection of large
  manifest content through formatting and search or navigation support.
- **FR-017**: Large validation, feature, setting, dependency, conflict, and
  manifest sections MUST provide in-page search and filtering.
- **FR-018**: Trust-changing actions MUST be scoped to the selected package
  version.
- **FR-019**: Approval confirmation MUST identify the package ID and selected
  version before the approval is submitted.
- **FR-020**: Rejection confirmation MUST identify the package ID and selected
  version and require a non-empty rejection reason before submission.
- **FR-021**: Optional operational actions, including revalidation, resync, and
  metadata recomputation, MUST only be available when supported for the selected
  version.
- **FR-022**: After a successful action, the page MUST refresh or otherwise
  update the affected version state so the administrator sees the current
  result.
- **FR-023**: If an action fails, the page MUST preserve the selected package and
  version context and show an actionable failure message.
- **FR-024**: If the selected version changed after the page loaded, the page
  MUST block trust-changing actions using a version state token or equivalent
  freshness marker, explain that the version state changed, and require refresh
  before retry.
- **FR-025**: The page MUST preserve direct-link usability for package details
  and version-specific details.
- **FR-026**: The page MUST support direct links to major sections for a
  selected package version, including summary, validation, features,
  dependencies, compatibility, manifest, and actions.
- **FR-027**: The page MUST provide navigation back to the package list without
  losing the administrator's broader dashboard context.
- **FR-028**: The page MUST remain usable for packages with at least 100 indexed
  versions, 200 features, 500 settings, and 1,000 validation findings.
- **FR-029**: The page MUST avoid presenting stale protected package data as
  current after access is denied or the administrator is no longer authorized.

### Key Entities

- **Package**: A catalog package identity. Package IDs resolve
  case-insensitively and display using canonical indexed casing. Key attributes
  include package ID, source, latest known version, indexed versions, created
  date, updated date, and aggregate operational status.
- **Package Version**: A versioned package record. Key attributes include
  version, approval status, validation status, listing state, suspicious state,
  manifest metadata, publication date, indexed date, and visibility state.
- **Visibility Reason**: A human-readable explanation for why a package version
  is visible or hidden. It relates to one selected package version and may be
  caused by trust decisions, validation, listing, source state, manifest state,
  or ingestion state.
- **Validation Finding**: A validation result for a selected package version.
  Key attributes include severity, code or rule identifier, message, affected
  field path, and whether the finding blocks visibility.
- **Feature**: A functional capability contributed by a package version. Key
  attributes include feature identity, display name, description, category,
  settings, dependencies, conflicts, and compatibility metadata.
- **Setting**: A configurable value exposed by a feature. Key attributes include
  setting identity, label, value type, required state, default value presence,
  and validation hints.
- **Manifest**: The package manifest content and identity metadata for a package
  version. Key attributes include schema version, manifest hash, raw content,
  and availability state.
- **Version Action**: A deliberate administrator operation against one package
  version, such as approve, reject, revalidate, resync, or recompute metadata.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of administrators can identify the selected package version
  and whether it is publicly visible within 10 seconds of opening the page.
- **SC-002**: 95% of seeded visibility-blocker combinations are explained by the
  page without requiring administrators to inspect raw data outside the
  dashboard.
- **SC-003**: Administrators can switch versions and see all version-scoped
  sections reflect the new version within 2 seconds for packages with up to 100
  indexed versions.
- **SC-004**: Administrators can find a specific validation finding or manifest
  field in under 30 seconds for a package version with 1,000 validation findings
  or a large manifest.
- **SC-005**: 100% of trust-changing actions shown by the page identify the
  exact package ID and version before submission.
- **SC-006**: 100% of rejection attempts without a non-empty reason are blocked
  before submission.
- **SC-007**: In usability review, at least 4 out of 5 administrators can explain
  why a hidden version is not visible after using only the package details page.
- **SC-008**: Page-level load, empty, not-found, access-denied, unavailable, and
  unexpected-error states are covered by acceptance tests.

## Assumptions

- Package details are part of the existing authenticated admin dashboard
  experience.
- Administrators already have permission to view package catalog operational
  data when they can access the dashboard.
- Package review and trust decisions continue to apply at package-version
  scope.
- The packages list or another dashboard entry point can link to package details.
- The page may use existing catalog data and existing admin capabilities; this
  specification does not require direct manifest editing.
- The first version supports inspection and deliberate actions, not advanced
  analytics, package marketplace presentation, or graphical dependency maps.
