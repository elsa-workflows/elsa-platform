# Feature Specification: Elsa Control Package Catalog Console UI

**Feature Branch**: `codex/004-admin-dashboard-ui`

**Created**: 2026-05-15

**Status**: Draft

**Input**: User description: "Create a specification for a lightweight console UI for the Elsa Control Package Catalog system."

## Overview

The Elsa Control Package Catalog Console is a lightweight operational web UI for
administrators who manage package sources, monitor synchronization activity,
inspect package manifests, review validation results, approve or reject packages,
and troubleshoot indexing problems. It consumes the authenticated Catalog Admin
APIs and presents catalog state in a calm, inspectable interface optimized for
quick operational decisions.

The dashboard is intentionally small. It is not an enterprise observability
platform, analytics suite, package marketplace, or Runtime Builder UI. It exists
to answer practical questions: whether synchronization is healthy, which sources
need attention, which packages are awaiting approval, why a package is hidden
from public APIs, what validation failed, and whether an administrator can safely
trigger a sync, revalidation, approval, or rejection.

The first version favors clear tables, concise status summaries, detail drawers
or pages, readable validation diagnostics, and predictable confirmation flows.
Polling-based refresh is acceptable. Real-time infrastructure, advanced metrics,
complex dashboards, role matrices, and large-scale visualization features are
out of scope.

## Clarifications

### Session 2026-05-15

- Q: Should package approval operate at package identity, package version, or both in the first UI slice? -> A: Package version approval only.
- Q: Should package version rejection require an administrator-provided reason? -> A: Rejection reason required.
- Q: Should Settings be included in the first dashboard slice? -> A: Defer Settings from MVP.
- Q: How should source deletion behave when source history or indexed packages may exist? -> A: Soft-delete.
- Q: Which source health fields are guaranteed by the admin API versus inferred from sync history? -> A: Status and last successful sync are guaranteed; other health details are inferred from recent sync runs when available.

## Goals

- Keep the admin experience small, operational, technical, and inspectable.
- Make package source management the clearest and most complete workflow.
- Make synchronization state understandable without building an observability
  platform.
- Make validation failures and suspicious package states easy to debug.
- Make package approval and rejection simple, deliberate, and auditable.
- Explain why packages or versions are hidden from public APIs.
- Provide a restrained visual direction suitable for internal technical users.
- Consume existing authenticated Catalog Admin APIs through straightforward
  request, polling, mutation, and error-handling patterns.
- Support responsive layouts, dark mode, keyboard accessibility, and fast
  perceived performance.

## Non-Goals

- Building the public package discovery UI.
- Building the Runtime Builder UI.
- Building enterprise BI dashboards, KPI dashboards, or metrics-heavy monitoring
  portals.
- Building real-time streaming observability or websocket-heavy infrastructure.
- Building distributed admin workflows or an admin plugin system.
- Adding advanced RBAC or permission modeling in the first version.
- Visualizing package dependency graphs.
- Supporting package installation, Docker image assembly, deployment bundle
  generation, or Nuplane execution.
- Editing manifests directly inside the dashboard.
- Replacing API documentation or a dedicated API explorer.
- Managing private NuGet credentials unless a later backend feature explicitly
  supports them.

## Personas

- **Catalog Administrator**: Configures package sources, triggers syncs, reviews
  pending packages, approves or rejects catalog entries, and investigates failed
  indexing.
- **Release Operator**: Checks whether recent package publishing activity has
  synchronized successfully and whether new versions are visible or blocked.
- **Package Publisher Support Engineer**: Inspects validation errors, manifest
  JSON, and source decisions when helping a publisher fix a package.
- **Catalog Maintainer**: Verifies source health, reviews suspicious manifest
  changes, and confirms the admin APIs expose the data needed for operations.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operate Package Sources (Priority: P1)

A Catalog Administrator can view, create, edit, enable, disable, soft-delete,
and manually synchronize package sources from one focused operational screen.

**Why this priority**: Source configuration controls which packages can enter
the catalog and is the primary daily operating workflow.

**Independent Test**: Start with no sources, create a source with include and
exclude patterns, verify the source appears in the list with health information,
edit it, test patterns against sample package IDs, disable it, re-enable it,
trigger sync, and soft-delete it after confirmation.

**Acceptance Scenarios**:

1. **Given** no package sources exist, **When** an administrator opens the
   Sources screen, **Then** the empty state explains that a source must be added
   before packages can be indexed and offers a primary add-source action.
2. **Given** an administrator creates a source with a name, feed URL, enabled
   state, approval policy, version discovery policy, include patterns, exclude
   patterns, and polling interval, **When** the source is saved successfully,
   **Then** the source list shows the new source with status, last sync, package
   count, enabled state, and available actions.
3. **Given** include and exclude patterns are being edited, **When** the
   administrator enters sample package IDs into the pattern tester, **Then** the
   preview clearly shows included and excluded matches, with excludes taking
   precedence.
4. **Given** a source has not synced recently or has health warnings, **When**
   the administrator opens its details, **Then** the dashboard shows last
   successful sync, current health, and the most relevant recent sync runs, with
   additional validation, authentication, or connectivity details inferred from
   recent runs when available.
5. **Given** a source is enabled, **When** the administrator chooses Disable,
   **Then** the dashboard requires confirmation and reflects that scheduled sync
   will skip the source after the update succeeds.
6. **Given** an administrator removes a source, **When** the source has sync
   history or indexed packages, **Then** the dashboard performs a soft-delete
   flow that removes it from active source management without implying
   historical package, validation, or sync records are erased.

---

### User Story 2 - Review and Approve Packages (Priority: P1)

A Catalog Administrator can browse indexed packages, filter by operational
state, inspect approval and validation status, and approve or reject selected
package versions. Rejection requires an administrator-provided reason.

**Why this priority**: Approval determines public catalog visibility and must
remain simple, deliberate, and separate from validation.

**Independent Test**: Seed approved, pending, rejected, invalid, suspicious, and
unlisted package versions, then verify search, filters, table state, package
details, single version approval, single version rejection, and bulk approval or
rejection behavior, including required rejection reasons.

**Acceptance Scenarios**:

1. **Given** packages exist in multiple approval and validation states, **When**
   an administrator opens the Packages screen, **Then** the table shows package
   ID, latest version, approval status, validation status, source, feature count,
   and updated timestamp.
2. **Given** packages are awaiting approval, **When** the administrator filters
   to Pending, **Then** only pending packages or versions are shown and the total
   result count is updated.
3. **Given** one or more package versions are selected, **When** the
   administrator uses Approve Selected or Reject Selected, **Then** a
   confirmation summarizes the affected versions and the table updates after the
   operation completes.
4. **Given** an administrator rejects one or more package versions, **When** the
   confirmation is shown, **Then** the dashboard requires a rejection reason
   before submitting the operation.
5. **Given** a package has validation failures, **When** the administrator opens
   package details, **Then** approval controls are still visible but public
   visibility remains blocked until validation is valid.
6. **Given** a package is hidden from public APIs, **When** the administrator
   views package details, **Then** the dashboard explains each reason such as
   validation failed, package not approved, version rejected, package unlisted,
   or suspicious manifest change.

---

### User Story 3 - Diagnose Package Manifests and Validation (Priority: P1)

A support engineer can inspect package metadata, validation errors, warnings,
suspicious manifest changes, and raw manifest JSON in one package detail view.

**Why this priority**: The dashboard must help operators explain and fix
package indexing problems without digging through logs or raw API responses.

**Independent Test**: Load a package with features, settings, compatibility
metadata, validation warnings, validation errors, and a raw manifest, then verify
that each diagnostic section is readable, searchable where appropriate, and
actionable.

**Acceptance Scenarios**:

1. **Given** a package has indexed manifest metadata, **When** an administrator
   opens its details, **Then** the Overview section shows package ID, versions,
   source, published date, indexed date, manifest hash, approval status, and
   validation status.
2. **Given** a manifest contains features and settings, **When** the Features
   section is viewed, **Then** feature names, setting counts, compatibility
   metadata, dependencies, and conflicts are presented in a compact inspectable
   format.
3. **Given** validation errors and warnings exist, **When** the Validation
   section is viewed, **Then** each issue shows severity, code, message, field
   path when available, and enough context to understand the failure.
4. **Given** a raw manifest exists, **When** the Manifest Viewer is opened,
   **Then** formatted JSON is shown with collapsible sections and a raw-view
   option without allowing direct editing.
5. **Given** a package is marked suspicious due to immutable-version manifest
   changes, **When** details are viewed, **Then** the dashboard highlights the
   suspicious reason and shows the relevant hashes or sync evidence exposed by
   the admin API.

---

### User Story 4 - Inspect Synchronization Runs (Priority: P2)

A Catalog Administrator can review sync runs, understand whether syncing is
healthy, and inspect run details, failures, warnings, and per-package outcomes.

**Why this priority**: Sync is the ingestion path for package data. Operators
need enough detail to debug failures without a full observability product.

**Independent Test**: Seed scheduled, manual all-source, manual source, and
manual package sync runs with completed, failed, running, canceled, and
completed-with-errors states, then verify summary rows, filtering, details,
cancel actions, and re-sync actions.

**Acceptance Scenarios**:

1. **Given** sync runs exist, **When** an administrator opens Sync Runs, **Then**
   the table shows started time, duration, associated source, trigger, status,
   packages scanned, packages updated, and failure count.
2. **Given** a sync run completed with errors, **When** the administrator opens
   run details, **Then** the dashboard shows summary counts, timeline entries,
   discovered packages, downloaded packages, validation results, warnings, and
   failures.
3. **Given** a sync run is active, **When** the dashboard refreshes, **Then** the
   active status updates through polling and never implies live log streaming.
4. **Given** a sync run is active, **When** the administrator chooses Cancel,
   **Then** the API requests cancellation for that run, the dashboard disables
   duplicate cancel submissions, and polling reflects the terminal Canceled
   status after the sync stops.
5. **Given** a run failed because a source was unreachable, **When** details are
   viewed, **Then** the failure is grouped under the affected source and includes
   the most actionable error message available.
6. **Given** a specific package failed during a run, **When** the administrator
   selects it, **Then** the dashboard links to package details when a package
   record exists and otherwise shows the sync item diagnostics.

---

### User Story 5 - Understand System State at a Glance (Priority: P2)

A Catalog Administrator can open a lightweight overview that answers whether the
catalog needs attention and links directly to the relevant operational screens.

**Why this priority**: A small overview reduces time-to-orientation without
turning the product into a dashboard-heavy analytics system.

**Independent Test**: Seed healthy sources, failed syncs, pending approvals,
invalid packages, and recent sync activity, then verify the overview surfaces
only concise operational status and links to filtered screens.

**Acceptance Scenarios**:

1. **Given** the catalog has healthy and unhealthy sources, **When** the Overview
   screen loads, **Then** it shows healthy source count, failed sync count,
   pending approvals, invalid package count, and last successful sync.
2. **Given** pending approvals exist, **When** the administrator selects the
   pending approvals summary, **Then** the Packages screen opens with the Pending
   filter applied.
3. **Given** recent sync activity exists, **When** the administrator views the
   overview, **Then** the latest relevant runs are shown without charts or
   long-term analytics.
4. **Given** no urgent issues exist, **When** the overview loads, **Then** it
   communicates calm healthy state without exaggerated success visuals.

---

### User Story 6 - Handle Admin API Errors Predictably (Priority: P2)

An administrator receives clear, recoverable feedback when authenticated admin
API calls fail, return stale data, or reject requested operations.

**Why this priority**: Admin operations affect catalog trust and must not fail
silently or leave the operator uncertain.

**Independent Test**: Simulate unauthorized responses, validation errors,
conflicts, network failures, long-running operations, empty responses, and
partial bulk failures, then verify visible states and recovery actions.

**Acceptance Scenarios**:

1. **Given** the admin session is invalid or expired, **When** an API request
   returns unauthorized, **Then** the dashboard shows an access problem and does
   not expose stale protected data as current.
2. **Given** a create or edit source request is rejected, **When** the API
   returns field-level errors, **Then** the form preserves entered values and
   displays errors beside the affected fields.
3. **Given** a bulk action partially succeeds, **When** the operation completes,
   **Then** the dashboard shows which items succeeded, which failed, and why.
4. **Given** data could not be refreshed, **When** an administrator is viewing a
   previously loaded screen, **Then** stale data is visually distinguished from a
   successful current refresh.

### Edge Cases

- No package sources exist.
- A package source has include patterns that match no sample package IDs.
- Exclude patterns remove every included package in the pattern tester.
- A source URL is malformed, unreachable, returns feed metadata errors, or
  requires unsupported credentials.
- A source is disabled while a sync run is already active.
- Manual sync is requested while the same source or package is already syncing.
- A package exists without a latest valid version.
- Multiple versions of the same package have different approval or validation
  states.
- A package is approved but invalid, rejected but valid, unlisted but approved,
  or suspicious regardless of approval.
- Validation results include many issues, missing field paths, unknown issue
  codes, or messages too long for a compact row.
- Raw manifest JSON is large but still within the catalog's accepted limits.
- Raw manifest JSON is malformed and cannot be formatted.
- The admin API returns paged data with an empty page after filters change.
- A bulk action is attempted on items that have changed state since selection.
- Polling refresh returns newer data while a form has unsaved changes.
- A soft-delete operation is requested for a source that still has indexed
  packages or sync history.
- A sync run contains item-level failures but the overall run completed.
- Dates arrive in UTC and must be displayed consistently with timezone context.
- The dashboard is used on a narrow screen where tables cannot fit comfortably.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The dashboard MUST provide authenticated admin-only access to
  operational catalog screens and MUST rely on the existing admin authentication
  boundary.
- **FR-002**: The dashboard MUST present a small primary navigation with
  Overview, Sources, Packages, and Sync Runs.
- **FR-003**: The MVP MUST defer Settings and use only Overview, Sources,
  Packages, and Sync Runs as primary destinations.
- **FR-004**: The dashboard MUST make Sources a primary workflow and MUST allow
  administrators to list, create, view, edit, enable, disable, soft-delete, and
  sync package sources when permitted by the admin API.
- **FR-005**: The Sources list MUST show name, type, URL, status, approval
  policy, last sync, package count, enabled state, and row actions.
- **FR-006**: Source create and edit forms MUST include name, feed URL, enabled
  state, approval policy, version discovery policy, include patterns, exclude
  patterns, and polling interval when those fields are supported by the admin
  API.
- **FR-007**: Source create and edit forms MUST include an include/exclude
  pattern tester that accepts sample package IDs and previews included and
  excluded outcomes.
- **FR-008**: The pattern tester MUST make exclude precedence visually obvious.
- **FR-009**: The dashboard MUST provide a source health view showing overall
  health, last successful sync, and recent sync activity.
- **FR-009a**: Source status and last successful sync MUST be treated as
  guaranteed source health fields; validation failure counts, authentication
  failures, and connectivity issues MUST be shown only when inferable from
  recent sync runs or explicit admin API diagnostics.
- **FR-010**: Source soft-delete actions MUST require confirmation and MUST
  explain that active source management changes while historical package,
  validation, and sync records may remain.
- **FR-010a**: The MVP MUST NOT expose hard-delete controls for package sources.
- **FR-011**: The Packages screen MUST allow administrators to browse indexed
  packages and versions with search, filtering, sorting, pagination or incremental
  loading, and bulk selection.
- **FR-012**: The Packages table MUST show package ID, latest version, approval
  status, validation status, source, features count, and updated timestamp.
- **FR-013**: Package filters MUST include approved, pending, rejected, invalid,
  suspicious, and unlisted states when those states are exposed by the admin API.
- **FR-014**: Package search MUST support package ID search and SHOULD preserve
  active filters while the query changes.
- **FR-015**: Package sorting MUST support at least package ID, updated time,
  approval status, validation status, and source when those fields are available.
- **FR-016**: Package bulk selection MUST persist only for the current filtered
  result set and MUST clearly show how many items are selected.
- **FR-017**: Bulk actions MUST include Approve Selected, Reject Selected, and
  Re-sync Selected for explicitly selected package versions where supported by
  the admin API.
- **FR-018**: Bulk actions MUST require confirmation, summarize affected items,
  report partial failures, and refresh affected rows after completion.
- **FR-018a**: Rejection actions for one or more package versions MUST require
  an administrator-entered reason before submission.
- **FR-019**: The Package Details screen MUST include Overview, Features,
  Validation, Manifest Viewer, Visibility Explanation, and Actions sections.
- **FR-020**: Package Overview MUST show package ID, available versions, source,
  published date when available, indexed date, manifest hash, approval status,
  and validation status.
- **FR-021**: Package Features MUST show feature list, setting counts,
  compatibility metadata, dependencies, and conflicts in an inspectable compact
  format.
- **FR-022**: Package Validation MUST show errors, warnings, missing metadata,
  unsupported property types, schema validation failures, and suspicious manifest
  changes when those diagnostics are available.
- **FR-023**: Validation issues MUST show severity, code, message, and field path
  when available.
- **FR-024**: The Manifest Viewer MUST provide formatted JSON inspection,
  collapsible sections, and raw manifest inspection without direct editing.
- **FR-025**: The Visibility Explanation section MUST explain why a package or
  version is hidden from public APIs, including validation failure, pending
  approval, rejection, unlisted state, or suspicious manifest state.
- **FR-026**: Package version actions MUST include Approve, Reject, Re-sync,
  Revalidate, and Recompute Metadata when those operations are supported by the
  admin API.
- **FR-026a**: The MVP approval workflow MUST operate on package versions only
  and MUST NOT introduce package identity approval controls.
- **FR-027**: The Sync Runs screen MUST list synchronization operations with
  started time, duration, trigger, status, packages scanned, packages updated,
  and failure count.
- **FR-028**: Sync trigger labels MUST distinguish scheduled, manual all-source,
  manual source, and manual package runs when that information is available.
- **FR-029**: Sync Run Details MUST show summary, timeline, operational log
  entries, discovered packages, downloaded packages, validation results,
  failures, and warnings when available.
- **FR-030**: Sync log presentation MUST optimize for debuggability and MUST NOT
  imply real-time streaming unless the underlying admin API supports it.
- **FR-031**: The Overview screen MUST show only lightweight operational status:
  healthy sources, failed syncs, pending approvals, invalid packages, last
  successful sync, recent sync activity, and concise system health indicators.
- **FR-032**: Overview summary items MUST link to the relevant filtered Sources,
  Packages, or Sync Runs screen.
- **FR-033**: The dashboard MUST use polling or explicit refresh for changing
  operational state and MUST avoid requiring real-time infrastructure in the MVP.
- **FR-034**: The dashboard MUST show loading, empty, error, stale, and
  refreshing states for each major screen.
- **FR-035**: The dashboard MUST preserve unsaved form input when a save request
  fails.
- **FR-036**: The dashboard MUST display field-level API validation errors beside
  matching fields when possible and show general request errors otherwise.
- **FR-037**: The dashboard MUST prevent duplicate submissions for create, edit,
  soft-delete, approval, rejection, sync, and revalidation operations while a
  request is pending.
- **FR-038**: The dashboard MUST clearly distinguish current data from data that
  failed to refresh.
- **FR-039**: The dashboard MUST provide consistent status badges for source
  health, sync status, approval status, validation status, listing state, and
  suspicious state.
- **FR-040**: The dashboard MUST avoid KPI-style charts and long-term analytics
  in the MVP.
- **FR-041**: The dashboard MUST support keyboard navigation for primary
  navigation, tables, filters, forms, dialogs, and action menus.
- **FR-042**: The dashboard MUST support dark mode without reducing status
  readability.
- **FR-043**: The dashboard MUST remain usable on mobile and narrow screens by
  adapting tables into horizontally scrollable, stacked, or priority-column
  layouts.
- **FR-044**: The dashboard MUST expose enough admin API interaction feedback for
  operators to understand whether an operation is pending, succeeded, failed, or
  partially failed.
- **FR-045**: The dashboard MUST NOT allow direct modification of raw manifests,
  validation results, sync history, manifest hashes, or immutable package version
  content.
- **FR-046**: The dashboard MUST NOT expose invalid, rejected, suspicious, or
  unlisted packages as public-safe; admin views MUST label them as operational
  records.
- **FR-047**: The dashboard MUST NOT include a Settings screen in the MVP;
  operational information needed for the first slice MUST appear in Overview,
  Sources, Packages, or Sync Runs.
- **FR-048**: The dashboard MUST display the deployed application build number
  in persistent dashboard chrome after administrator authentication.
- **FR-049**: The dashboard MUST keep framework-specific implementation choices
  outside the user experience contract; the specification permits a modern web
  stack but does not require one.

### Navigation Structure

The MVP navigation MUST use four primary destinations:

- **Overview**: Lightweight operational status and links into filtered screens.
- **Sources**: Source management, health, pattern testing, and source sync.
- **Packages**: Package browsing, package-version approval workflows,
  diagnostics, and details.
- **Sync Runs**: Sync history and run-level troubleshooting.

### Page Layouts

#### Overview

Overview uses a compact status area rather than a dense analytics dashboard.

Content:

- Healthy sources count.
- Failed sync count.
- Pending approval count.
- Invalid package count.
- Last successful sync.
- Recent sync activity.
- System health indicators such as catalog API reachability and indexing
  scheduler status when exposed.

Behavior:

- Each summary item links to a filtered operational screen.
- Counts should be informational, not gamified.
- Recent activity should be a short list, not a chart.
- The page should fit comfortably above the fold on common desktop screens.

#### Future: CShell Feature Management

The Control Plane Modules area should evolve from a static roadmap list into a
runtime feature-management surface backed by CShells
([valence-works/cshells](https://github.com/valence-works/cshells)). This is a
post-MVP capability because it requires backend and application-hosting changes,
not only console UI work.

Product goal:

- Administrators can see which platform features are available for the selected
  shell, organization, or workspace context.
- Administrators can enable or disable supported CShell features without editing
  configuration files directly.
- Administrators can configure public, runtime-safe feature properties through
  typed forms generated from backend metadata.
- The platform exposes enough dependency, validation, audit, and restart-impact
  information for operators to understand the effect of enabling, disabling, or
  reconfiguring a feature.

Architecture requirements:

- The Elsa Control application MUST be hosted through CShells before this
  feature-management UI is implemented. If the platform is not already powered by
  CShells, a prerequisite backend slice MUST migrate platform modules into
  CShell-compatible shell features.
- The backend MUST expose a REST API to list available CShell features for the
  current shell/scope, including feature identity, display metadata, category,
  source assembly/package, enabled state, dependencies, conflicts, capability
  tags, and whether the feature supports runtime enable/disable without process
  restart.
- The backend MUST expose public configurable properties for each feature using
  schema-like metadata: property name, display name, description, type,
  validation rules, default value, current value when safe, sensitivity flags,
  whether changes require restart, and whether the property can be changed after
  the feature is enabled.
- The backend MUST expose commands to enable, disable, and update feature
  configuration for the selected shell/scope. Commands MUST validate
  dependencies and conflicts before applying changes.
- The backend MUST persist feature state and feature property values in a
  provider-backed configuration store rather than treating appsettings.json as
  the only source of truth.
- The backend MUST never return raw secret values. Secret-like feature
  properties MUST use secret references or write-only update semantics.
- The backend MUST emit audit/history records for feature enablement,
  disablement, configuration changes, validation failures, and restart-required
  outcomes.
- If a CShell feature cannot be applied at runtime, the API response MUST report
  the required restart or redeploy action instead of pretending the change is
  live.

Console requirements:

- Control Plane Modules MUST become a real feature list/detail workflow when the
  backend feature API exists.
- The overview module cards MUST reflect live feature availability and status
  rather than static roadmap copy.
- A dedicated feature detail page SHOULD show dependencies, conflicts, public
  configuration properties, current runtime status, recent changes, and the
  exact validation result of a pending enable/disable/configuration action.
- Enable/disable controls MUST be explicit commands with validation feedback and
  confirmation for disruptive changes.
- Configuration editing MUST use dedicated forms, not inline edits inside the
  read-only feature overview.

Out of scope for this PRD amendment:

- Arbitrary third-party plugin installation.
- Editing raw shell JSON or appsettings files in the browser.
- Displaying or editing raw secret values.
- Claiming runtime enablement for features that require application restart or
  redeploy.

#### Sources

Sources is the primary operational screen.

Table columns:

- Name.
- Type.
- URL.
- Status.
- Approval policy.
- Last sync.
- Package count.
- Enabled.
- Actions.

Actions:

- Sync now.
- Edit.
- Disable or enable.
- Soft-delete.

Create/Edit form:

- Name.
- Feed URL.
- Enabled.
- Approval policy.
- Version discovery policy.
- Include patterns.
- Exclude patterns.
- Polling interval.
- Pattern tester.

Source details:

- Health card.
- Configuration summary.
- Recent sync runs.
- Recent validation or connectivity issues.

#### Packages

Packages supports operational browsing and approval.

Table columns:

- Package ID.
- Latest version.
- Approval status.
- Validation status.
- Source.
- Features count.
- Updated at.

Filters:

- Approved.
- Pending.
- Rejected.
- Invalid.
- Suspicious.
- Unlisted.

Actions:

- Open details.
- Approve.
- Reject.
- Re-sync.
- Bulk approve.
- Bulk reject.
- Bulk re-sync.

#### Package Details

Package Details uses sections or tabs depending on available space:

- Overview.
- Features.
- Validation.
- Manifest Viewer.
- Visibility Explanation.
- Actions.

Primary action area:

- Approve.
- Reject.
- Re-sync.
- Revalidate.
- Recompute metadata if applicable.

Visibility Explanation should be prominent whenever the package is hidden from
public APIs.

#### Sync Runs

Sync Runs shows synchronization history and outcome.

Table columns:

- Started at.
- Duration.
- Trigger.
- Status.
- Packages scanned.
- Packages updated.
- Failures.

Run details:

- Summary.
- Timeline.
- Operational log entries.
- Discovered packages.
- Downloaded packages.
- Validation results.
- Failures.
- Warnings.

### UX Flows

#### Add a Package Source

1. Administrator opens Sources.
2. Administrator selects Add Source.
3. Dashboard opens a focused form.
4. Administrator enters name, feed URL, enabled state, approval policy, version
   discovery policy, include patterns, exclude patterns, and polling interval.
5. Administrator tests patterns against sample package IDs.
6. Dashboard previews included and excluded IDs.
7. Administrator saves.
8. Dashboard validates fields locally where obvious, submits to admin API, shows
   field errors if rejected, and returns to the updated source list if saved.

#### Soft-Delete a Package Source

1. Administrator opens Sources.
2. Administrator chooses Remove for a source.
3. Dashboard shows a confirmation that explains the source will leave active
   source management while historical package, validation, and sync records may
   remain.
4. Administrator confirms.
5. Dashboard submits the soft-delete request and removes the source from the
   default active source list after success.

#### Approve Pending Package Versions

1. Administrator opens Packages.
2. Administrator applies Pending filter.
3. Administrator reviews rows and opens details for any uncertain package
   version.
4. Administrator selects one or more package versions.
5. Administrator chooses Approve Selected.
6. Dashboard shows confirmation with selected count and any warnings such as
   invalid or suspicious records.
7. Administrator confirms.
8. Dashboard submits the operation, reports success or partial failure, and
   refreshes affected rows.

#### Reject Package Versions

1. Administrator opens Packages or Package Details.
2. Administrator selects one or more package versions.
3. Administrator chooses Reject.
4. Dashboard shows confirmation with selected count and a required rejection
   reason field.
5. Administrator enters a short reason and confirms.
6. Dashboard submits the operation, reports success or partial failure, and
   shows the rejection reason in package version details when available.

#### Investigate a Validation Failure

1. Administrator opens Overview or Packages and follows an invalid package link.
2. Dashboard opens Package Details.
3. Validation section shows errors and warnings with severity, code, message,
   and field path.
4. Administrator opens Manifest Viewer to inspect the relevant JSON.
5. Administrator optionally triggers Revalidate or Re-sync if appropriate.
6. Dashboard shows operation result and updated validation status.

#### Troubleshoot a Failed Sync

1. Administrator opens Sync Runs or a source health card.
2. Administrator selects a failed or completed-with-errors run.
3. Dashboard shows summary, timeline, and grouped failures.
4. Administrator identifies affected source or package.
5. Administrator follows links to source or package details.
6. Administrator triggers Sync Now or Re-sync Package after correcting the
   underlying source configuration or package issue.

## API Interaction Patterns

- The dashboard consumes authenticated Catalog Admin APIs only.
- Initial screen load should request only the data needed for that screen.
- Lists should use server-supported pagination, filtering, sorting, and search
  when available.
- Mutations should be explicit, confirm destructive or trust-changing actions,
  and refresh affected data after completion.
- Polling is acceptable for overview, source health, active sync runs, and
  recently changed package statuses.
- Polling intervals should be modest and should pause or reduce activity when
  the screen is not visible if supported by the runtime environment.
- Manual refresh should be available on operational lists and detail screens.
- API errors should be mapped into unauthorized, validation, conflict,
  not-found, rate or availability, and unexpected categories when possible.
- Conflict responses should explain that the record changed and prompt the
  administrator to refresh before retrying.
- Bulk operation responses should support item-level success and failure
  reporting.
- Detail screens should tolerate partial data by showing available sections and
  clear unavailable states for missing sections.
- Date and duration values should be displayed consistently and retain enough
  timezone context for operational troubleshooting.

## Table, Filter, and Search Behavior

- Tables should default to the most operationally useful ordering: recent
  attention-needed records first for Packages and Sync Runs, source name or
  health state for Sources.
- Search and filters should be composable.
- Active filters should be visible and easy to clear.
- Empty filtered results should distinguish "nothing exists" from "nothing
  matches these filters".
- Table rows should expose a primary detail action and a compact action menu.
- Status badges should use consistent language and color semantics across
  screens.
- Long package IDs, URLs, validation messages, and source names should truncate
  gracefully with full text available on demand.
- Selection should be explicit and should reset or warn when filters change in a
  way that removes selected items from view.
- Sorting should never hide active filters or selected item counts.

## Bulk Action Behavior

- Bulk actions apply only to explicitly selected rows.
- Bulk action confirmations must show selected count, action name, and any known
  risk states such as invalid, rejected, suspicious, or already approved.
- Bulk approval must not imply validation success.
- Bulk rejection must require an administrator-entered reason and apply that
  reason to each selected package version unless the admin API returns
  item-specific rejection failures.
- Bulk re-sync must explain that results may take time and may not change package
  state immediately.
- Partial failures must be shown at item level.
- Successful items should be refreshed without forcing the administrator to
  rebuild the current filter state.

## Validation UX

- Validation failures should be grouped by severity first, then by code or field
  path.
- Errors should be visually stronger than warnings but should not use alarming or
  noisy presentation.
- Field paths should be copyable when available.
- Issue codes should remain visible because operators and package publishers may
  reference them in support conversations.
- Long messages should wrap and remain readable.
- Unknown validation codes should still be shown without breaking the layout.
- Missing metadata, unsupported property types, schema validation failures, and
  suspicious manifest changes should be distinguishable.
- The raw manifest should be inspectable from the same details page as validation
  results.

## Error Handling

- Unauthorized access should show a clear access problem and avoid rendering
  protected stale data as current.
- Not-found states should explain that the item may have been deleted or changed.
- Validation errors should appear beside form fields when a field mapping exists.
- Conflicts should prompt refresh and retry.
- Network or service availability errors should show retry and manual refresh
  options.
- Unexpected errors should include a concise message and avoid exposing sensitive
  implementation details.
- Destructive and trust-changing operations should show a pending state and
  prevent duplicate submissions.
- Stale data should be labeled when refresh fails after previous data was loaded.

## Empty, Loading, and Refreshing States

- Loading states should use stable skeletons or placeholders that preserve page
  shape.
- Empty Sources should invite adding a source.
- Empty Packages should explain that no packages have been indexed yet and link
  to Sources.
- Empty Sync Runs should explain that no synchronization has run yet and link to
  source sync actions.
- Empty filtered results should show active filters and a clear-filters action.
- Refreshing should be quieter than initial loading and should not block reading
  already loaded data.
- Long-running operations should show pending status and a way to continue
  navigating when safe.

## Visual Design Direction

- The dashboard should feel calm, technical, predictable, and trustworthy.
- Visual references include restrained operational tools such as Linear, GitHub,
  Vercel, and Supabase dashboards.
- Layout should use a simple sidebar or top-level navigation, compact content
  headers, clear tables, restrained cards for summary status, and focused detail
  views.
- Cards should be used for concise summaries and repeated records, not as nested
  decoration.
- Colors should support status recognition but avoid loud palettes, heavy
  gradients, and decorative visual noise.
- Typography should prioritize scanning, readable diagnostics, and stable table
  layout.
- Animation should be minimal and functional.
- The Overview screen should not become a marketing-style hero or analytics
  command center.

## Accessibility Considerations

- All primary workflows must be keyboard navigable.
- Focus order must follow visual and task order.
- Dialogs must trap focus while open and restore focus after close.
- Status badges must not rely on color alone.
- Tables must expose accessible row labels and actions.
- Form fields must have labels, descriptions where useful, and associated error
  messages.
- Validation issue lists must be readable by assistive technology.
- Collapsible manifest sections must expose expanded and collapsed state.
- Dark mode contrast must meet accessible contrast expectations for text,
  controls, and status indicators.

## Mobile Responsiveness Considerations

- Mobile and narrow screens should preserve core operational workflows even if
  dense table comparison is less comfortable.
- Navigation should collapse without hiding primary destinations.
- Tables should adapt through priority columns, row cards, or horizontal
  scrolling with stable headers.
- Action menus and confirmations must remain usable on touch screens.
- Source forms and pattern tester previews should stack cleanly.
- Manifest viewer should remain readable with line wrapping or horizontal
  scrolling options.
- Bulk actions may be reduced to selected-count bars or action menus on narrow
  screens.

## Security Considerations

- The dashboard must assume admin APIs are authenticated and must not weaken that
  boundary.
- The dashboard must not show protected data after unauthorized responses as if
  it were current.
- Raw manifests, validation messages, source URLs, and error details should be
  treated as operational data and displayed only in authenticated admin context.
- Destructive actions and trust-changing actions require confirmation.
- Direct manifest editing is forbidden in the MVP.
- The dashboard must not execute package code, load package assemblies, or
  evaluate manifest content as executable behavior.
- Any copied diagnostic content should be plain text and not include secrets
  unless the backend intentionally exposes them.

## Testing Strategy

- Test each primary navigation destination loads with loading, populated, empty,
  filtered-empty, error, and stale-refresh states.
- Test source create, edit, enable, disable, soft-delete, sync, and pattern
  tester workflows.
- Test package search, filtering, sorting, details, approval, rejection, re-sync,
  required rejection reason, revalidation, visibility explanation, and bulk
  action workflows.
- Test sync run list and detail screens for completed, failed, running, and
  completed-with-errors states.
- Test validation issue rendering for errors, warnings, missing field paths,
  long messages, unknown codes, and malformed manifest JSON.
- Test API interaction states for unauthorized, not found, validation rejection,
  conflict, network failure, unexpected failure, and partial bulk failure.
- Test accessibility with keyboard-only navigation, focus management, labels,
  status semantics, and dark mode contrast.
- Test responsive behavior on desktop, tablet-width, and narrow mobile layouts.
- Test that invalid, rejected, suspicious, pending, and unlisted states are never
  presented as publicly visible or public-safe.

### Key Entities *(include if feature involves data)*

- **Package Source**: A configured package feed with name, type, URL, enabled
  state, approval policy, version discovery policy, include patterns, exclude
  patterns, polling interval, health status, last sync timestamps, package
  counts, and soft-delete state.
- **Source Health**: Operational status for a source. Source status and last
  successful sync are guaranteed fields; validation counts, authentication
  failures, connectivity issues, and diagnostic messages are derived from recent
  sync runs or explicit admin API diagnostics when available.
- **Package**: An indexed package identity with source relationship, latest
  version, aggregate validation and listing indicators, feature count,
  suspicious state, and updated timestamp.
- **Package Version**: A specific package version with manifest hash, published
  date, indexed date, approval state, validation result, visibility state, and
  immutable-version diagnostics when applicable. Approval and rejection decisions
  apply at this level in the MVP, and rejected versions require an
  administrator-provided rejection reason.
- **Feature Metadata**: Manifest-derived feature information including feature
  identity, display metadata, settings count, compatibility metadata,
  dependencies, and conflicts.
- **Validation Result**: Errors and warnings associated with a package version or
  manifest, including severity, code, message, field path, and contextual
  metadata.
- **Manifest**: Raw and formatted `elsa-package.json` content for inspection.
- **Sync Run**: A synchronization operation with trigger, status, start and
  completion timestamps, duration, scanned and updated package counts, failures,
  warnings, and item-level diagnostics.
- **Admin Operation**: A requested mutation such as approve, reject, sync,
  revalidate, edit source, disable source, or soft-delete source, including
  pending, success, failure, and partial-failure outcomes.
- **Rejection Reason**: A short administrator-entered explanation recorded when
  rejecting one or more package versions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can create a package source, validate sample
  include/exclude behavior, save it, and trigger its first sync in under 3
  minutes during usability testing.
- **SC-002**: An administrator can identify all package versions awaiting
  approval and approve or reject selected versions with a rejection reason in
  under 2 minutes for a list of 50 packages.
- **SC-003**: An administrator can determine why a hidden package is not visible
  through public APIs in under 30 seconds from the package details screen.
- **SC-004**: An administrator can locate validation errors for an invalid
  package and inspect the corresponding manifest content in under 60 seconds.
- **SC-005**: An administrator can identify the cause of a failed sync run in
  under 90 seconds when the admin API provides item-level diagnostics.
- **SC-006**: At least 90% of primary admin workflows remain completable with
  keyboard-only navigation in accessibility testing.
- **SC-007**: Initial screen content for common admin lists appears within 2
  seconds on a typical internal network for datasets up to 100 sources, 5,000
  packages, and 1,000 recent sync runs when the admin API responds normally.
- **SC-008**: The MVP contains no analytics-only charts, dependency graph
  visualizations, realtime streaming log views, advanced RBAC screens, or plugin
  management surfaces.

## Acceptance Criteria

- Overview provides concise operational status and links to filtered operational
  screens without analytics-heavy presentation.
- Sources supports full source lifecycle management including soft-delete,
  source health inspection, sync-now action, and include/exclude pattern
  testing.
- Packages supports search, filters, sorting, package details, visibility
  explanation, single version approval/rejection, and bulk version
  approval/rejection/re-sync.
- Package Details makes manifest diagnostics, validation results, raw manifest
  inspection, and hidden-state explanations available in one coherent workflow.
- Sync Runs supports list and detail inspection with enough timeline and item
  diagnostics to troubleshoot indexing problems.
- The MVP omits Settings and keeps operational information within Overview,
  Sources, Packages, and Sync Runs.
- Loading, empty, error, stale, and partial-failure states are implemented for
  the primary screens and mutation workflows.
- Keyboard accessibility, dark mode, and responsive behavior are verified for
  all primary workflows.
- The dashboard does not introduce enterprise dashboard complexity, real-time
  infrastructure requirements, advanced RBAC, dependency graph visualizations, or
  direct manifest editing.

## Assumptions

- Catalog Admin APIs already exist, are authenticated, and expose enough data for
  sources, packages, validation results, manifests, approval operations, and sync
  runs.
- The first dashboard version is used by trusted internal administrators.
- Polling and manual refresh are sufficient for operational updates.
- Package source credentials are not managed by the dashboard in the MVP.
- Approval and validation remain separate backend concepts, and the dashboard
  reflects that separation using package-version approval decisions in the MVP.
- Public API visibility is determined by backend rules; the dashboard explains
  visibility but does not reimplement policy as an authority.
- REST-style request and response examples are representative of the admin API
  shape, but the exact endpoint names are defined outside this UX spec.
- A modern web UI stack may be used, but framework selection is an implementation
  decision for planning rather than a user-facing requirement.
