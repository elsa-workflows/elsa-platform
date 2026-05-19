# Feature Specification: Delete Sync Runs

**Feature Branch**: `005-delete-sync-runs`

**Created**: 2026-05-16

**Status**: Draft

**Input**: User description: "Create a new feature that lets us delete sync runs to avoid excessive database growth with information that gets useless over time."

## Overview

Catalog administrators need a safe way to remove obsolete synchronization run history after it has served its debugging purpose. Sync run records and their item-level details can grow quickly over time, especially when scheduled synchronization runs frequently. This feature lets administrators delete old sync runs while preserving the package catalog state, approval state, validation results, and any current sync activity that still matters operationally.

## Clarifications

### Session 2026-05-16

- Q: Which sync run statuses are eligible for cleanup? → A: All terminal runs are eligible: `Completed`, `CompletedWithErrors`, and `Failed`; non-terminal runs are protected.
- Q: How should future bulk cleanup cutoffs be handled? → A: Reject future bulk cleanup cutoffs with a validation error.
- Q: Should direct single-run deletion have an age cutoff? → A: Direct single-run deletion may delete any terminal run by ID, regardless of age.

## Goals

- Reduce long-term database growth caused by obsolete sync run history.
- Let administrators delete one sync run or a filtered set of old sync runs.
- Prevent deletion of active sync runs or catalog records that are not sync history.
- Make cleanup actions visible enough for operators to understand what was removed.
- Keep recent history available for troubleshooting by default.

## Non-Goals

- Deleting packages, package versions, manifests, validation results, approvals, or source configuration.
- Changing how sync runs are created, scheduled, or processed.
- Adding a legal or compliance-grade audit archive.
- Automatically deleting all sync history without administrator intent.
- Compressing or moving sync history to external storage.

## Personas

- **Catalog Administrator**: Reviews synchronization history, diagnoses ingestion issues, and removes obsolete run records to manage storage growth.
- **Operations Maintainer**: Monitors database size and needs cleanup controls that reduce storage without damaging catalog correctness.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Delete One Obsolete Sync Run (Priority: P1)

A Catalog Administrator can delete a terminal sync run that is no longer useful, including its item-level details, while keeping all package catalog data intact.

**Why this priority**: Single-run deletion is the smallest safe cleanup capability and proves that cleanup can be scoped without affecting catalog state.

**Independent Test**: Create a terminal sync run with item-level details and related package catalog records, delete that run, then verify the run and items are gone while packages, versions, manifests, validation results, approvals, and sources remain available.

**Acceptance Scenarios**:

1. **Given** a terminal sync run with item-level details, **When** an administrator deletes that sync run, **Then** the sync run and its item details are removed from sync history.
2. **Given** a deleted sync run previously indexed package versions, **When** an administrator reviews packages and versions afterward, **Then** the catalog records remain unchanged.
3. **Given** a sync run does not exist, **When** an administrator requests deletion, **Then** the system reports that there is no matching run and does not change other data.

---

### User Story 2 - Delete Old Sync Runs in Bulk (Priority: P1)

A Catalog Administrator can delete multiple terminal sync runs older than a chosen cutoff so routine scheduled runs do not cause unbounded database growth.

**Why this priority**: Storage growth is the driver for the feature; cleanup needs to handle accumulated history without repetitive manual deletion.

**Independent Test**: Seed recent and old sync runs with multiple statuses, delete runs older than a cutoff, then verify only eligible old terminal history was removed and recent or ineligible runs remain visible.

**Acceptance Scenarios**:

1. **Given** several terminal sync runs are older than a selected cutoff, **When** an administrator confirms bulk deletion for that cutoff, **Then** all eligible runs and their item details are removed.
2. **Given** recent sync runs are newer than the selected cutoff, **When** bulk deletion completes, **Then** those recent runs remain visible.
3. **Given** the selected cutoff matches no eligible sync runs, **When** bulk deletion is requested, **Then** the system reports that zero runs were deleted.

---

### User Story 3 - Protect Active and Important History (Priority: P2)

The system prevents deletion of sync runs that are still active, and it clearly summarizes what will and will not be deleted before cleanup proceeds.

**Why this priority**: Cleanup must not make active operations harder to diagnose or corrupt an in-progress sync.

**Independent Test**: Attempt to delete active, queued, completed, failed, and canceled runs through single and bulk deletion flows, then verify active or queued runs remain while eligible terminal runs can be removed.

**Acceptance Scenarios**:

1. **Given** a sync run is currently active or queued, **When** an administrator tries to delete it, **Then** the deletion is refused and the run remains available.
2. **Given** bulk deletion would include both eligible terminal runs and active runs, **When** the cleanup is evaluated, **Then** active runs are excluded and the administrator sees counts for eligible and excluded runs.
3. **Given** an administrator confirms deletion, **When** cleanup finishes, **Then** the system reports how many runs and item records were deleted.

### Edge Cases

- A sync run starts or changes status while a bulk deletion request is being evaluated.
- A delete request references a sync run that was already deleted by another administrator.
- A direct delete request targets a recent terminal sync run; the request is allowed because the administrator selected that run by identity.
- A bulk deletion cutoff is in the future; the request is rejected with a validation error and no sync history is deleted.
- A bulk deletion request covers all historical sync runs.
- A sync run has a failed or canceled status but still contains item-level diagnostic details.
- Sync run item records exist without expected parent records due to prior data inconsistency.
- Two cleanup requests overlap in time.
- Time-based filters are supplied in local time or without timezone information.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow authorized administrators to delete a specific sync run by identity.
- **FR-001a**: Direct single-run deletion MUST allow any terminal sync run to be deleted by identity regardless of age.
- **FR-002**: The system MUST allow authorized administrators to delete sync runs older than a selected cutoff.
- **FR-003**: Sync run deletion MUST remove the selected sync run records and their item-level sync history details.
- **FR-004**: Sync run deletion MUST NOT remove or modify package sources, packages, package versions, manifests, validation results, approval records, public catalog visibility, or source configuration.
- **FR-005**: The system MUST allow deletion of terminal sync runs with status `Completed`, `CompletedWithErrors`, or `Failed`, and MUST refuse deletion of active, queued, or otherwise non-terminal sync runs.
- **FR-006**: Bulk deletion MUST exclude ineligible sync runs and report how many runs were eligible, excluded, and deleted.
- **FR-007**: Before bulk deletion proceeds, administrators MUST be able to see the deletion scope, including the cutoff and the number of eligible sync runs.
- **FR-008**: The system MUST report the result of each deletion request, including deleted sync run count and deleted item detail count.
- **FR-009**: If a requested sync run is missing or already deleted, the system MUST return a clear no-match outcome without deleting unrelated data.
- **FR-010**: The system MUST make deletion behavior idempotent for repeated requests against the same already-deleted sync run or already-cleaned cutoff.
- **FR-011**: The system MUST record administrative cleanup activity in the normal operational logs, including who initiated deletion when the acting administrator is known, the cleanup scope, and deletion counts.
- **FR-012**: All cutoff comparisons MUST use UTC timestamps.
- **FR-012a**: Bulk cleanup MUST reject cutoff timestamps later than the current server time and MUST NOT delete any sync history for such requests.
- **FR-013**: Deletion MUST preserve enough recent sync history by default by requiring an explicit administrator-selected cutoff for bulk cleanup.
- **FR-014**: Cleanup operations MUST be safe to run while other administrators are viewing sync history.
- **FR-015**: Cleanup operations MUST avoid partial deletion results where sync run headers are removed but their item-level details remain visible as ordinary sync history.

### Key Entities

- **Sync Run**: A historical record of a synchronization attempt, including trigger, status, start time, completion time, summary counters, and error summary.
- **Sync Run Item**: Item-level diagnostic details for a sync run, such as source, package, version, outcome, and failure or validation notes.
- **Cleanup Request**: Administrator intent to delete either one sync run or all eligible runs before a selected cutoff.
- **Cleanup Result**: Outcome summary for a cleanup request, including eligible, excluded, deleted, and no-match counts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Administrators can delete a single terminal sync run and verify within 30 seconds that it no longer appears in sync history.
- **SC-002**: Administrators can delete at least 1,000 eligible historical sync runs in one cleanup request without changing any package catalog records.
- **SC-003**: 100% of active or queued sync runs remain undeleted when included in direct or bulk deletion attempts.
- **SC-004**: After cleanup, package discovery and admin package review show the same package, version, approval, and validation state as before cleanup.
- **SC-005**: A cleanup result always reports deleted run count and deleted item detail count so operators can reconcile expected storage reduction.

## Assumptions

- Sync runs have terminal states such as completed, completed with errors, or failed, and non-terminal states such as queued or running.
- Sync run history is useful for recent troubleshooting but becomes less valuable as it ages.
- Administrators are already authenticated through the existing protected admin surface.
- Bulk deletion is manually initiated in the first version; automatic scheduled retention can be added later if needed.
- Deleting sync run history is acceptable because package catalog state and operational logs remain the source for current catalog behavior and cleanup accountability.
