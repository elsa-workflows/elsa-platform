# Data Model: Delete Sync Runs

## Sync Run

Existing synchronization execution record.

Fields relevant to cleanup:

- `id`: Unique run identity.
- `status`: Current run state.
- `startedAt`: UTC start timestamp.
- `completedAt`: UTC completion timestamp, required for cutoff eligibility.
- `items`: Item-level diagnostic history owned by the run.

Rules:

- Eligible for deletion only when `status` is terminal.
- Eligible for bulk cleanup only when `completedAt` is before the selected UTC cutoff.
- Deleting a sync run deletes its owned sync run items.
- Deleting a sync run must not delete package catalog state referenced by its items.

State classification:

- Non-terminal: `Running`.
- Terminal: `Completed`, `CompletedWithErrors`, `Failed`.
- Future statuses must be explicitly classified before becoming cleanup-eligible.

## Sync Run Item

Existing item-level diagnostic detail for one sync run.

Fields relevant to cleanup:

- `id`: Unique item identity.
- `syncRunId`: Owning sync run.
- `sourceId`: Optional source reference.
- `packageId`: Optional package identity.
- `version`: Optional package version string.
- `packageVersionId`: Optional catalog package version reference.
- `status`: Item outcome.

Rules:

- Removed when the owning sync run is deleted.
- Must not force deletion of referenced package versions.
- Orphaned item records, if found by persistence cleanup, may be removed only when they are no longer attached to visible sync history.

## Sync Run Cleanup Preview

Server-computed summary of what a bulk cleanup would affect.

Fields:

- `completedBefore`: UTC cutoff supplied by the administrator.
- `eligibleRunCount`: Terminal runs before the cutoff.
- `eligibleItemCount`: Item records belonging to eligible runs.
- `excludedRunCount`: Matching or inspected runs excluded because they are non-terminal or not completed before the cutoff.
- `oldestEligibleCompletedAt`: Oldest eligible completion timestamp, when any.
- `newestEligibleCompletedAt`: Newest eligible completion timestamp, when any.

Rules:

- Does not mutate data.
- Uses the same eligibility rules as bulk deletion.
- Counts are advisory because concurrent sync completion or cleanup can change the final delete result.

## Sync Run Cleanup Request

Administrator intent to delete sync history.

Variants:

- Single-run cleanup by `syncRunId`.
- Bulk cleanup by `completedBefore` cutoff.

Rules:

- Requires admin authorization.
- Bulk cleanup requires an explicit cutoff.
- Cutoff comparisons use UTC.
- Bulk cleanup cutoffs later than the current server time are rejected before any deletion occurs.

## Sync Run Cleanup Result

Outcome returned after deletion.

Fields:

- `deletedRunCount`: Number of sync run records removed.
- `deletedItemCount`: Number of sync run item records removed.
- `excludedRunCount`: Runs not deleted because they were ineligible.
- `notFoundRunCount`: Requested single run count that did not exist or was already deleted.
- `completedBefore`: Cutoff for bulk cleanup, when applicable.
- `deletedRunIds`: Deleted run identities for single-run cleanup and optionally for small bulk results.

Rules:

- Repeating an already-successful deletion returns a no-match or zero-delete outcome without deleting unrelated data.
- Result counts are based on the committed cleanup operation, not only on preview counts.
