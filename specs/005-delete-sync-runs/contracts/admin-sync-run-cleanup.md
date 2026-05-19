# Contract: Admin Sync Run Cleanup

All endpoints are under the existing authenticated admin API surface and require the existing admin authorization policy.

## GET /api/admin/sync-runs/deletion-preview

Preview bulk deletion scope.

Query parameters:

- `completedBefore`: Required UTC timestamp. Runs completed before this timestamp are candidates for cleanup.

Expected behavior:

- `200 OK` with `AdminSyncRunCleanupPreviewResponse` when the cutoff is valid.
- `400 Bad Request` when `completedBefore` is missing or cannot be interpreted as an absolute timestamp.
- `400 Bad Request` when `completedBefore` is later than the current server time.
- Does not delete or modify any data.

Response shape:

```json
{
  "completedBefore": "2026-05-16T00:00:00Z",
  "eligibleRunCount": 42,
  "eligibleItemCount": 1234,
  "excludedRunCount": 1,
  "oldestEligibleCompletedAt": "2026-04-01T12:00:00Z",
  "newestEligibleCompletedAt": "2026-05-01T12:00:00Z"
}
```

## DELETE /api/admin/sync-runs/{id}

Delete one sync run.

Path parameters:

- `id`: Sync run identifier.

Expected behavior:

- `200 OK` with `AdminSyncRunCleanupResultResponse` when the run is deleted or when the request is an idempotent no-match.
- `409 Conflict` when the run exists but is non-terminal, such as `Running`.
- Package sources, packages, package versions, manifests, validation results, approvals, and listing state are unchanged.

Response shape:

```json
{
  "deletedRunCount": 1,
  "deletedItemCount": 8,
  "excludedRunCount": 0,
  "notFoundRunCount": 0,
  "completedBefore": null,
  "deletedRunIds": ["11111111-1111-1111-1111-111111111111"]
}
```

Already-deleted or missing run response:

```json
{
  "deletedRunCount": 0,
  "deletedItemCount": 0,
  "excludedRunCount": 0,
  "notFoundRunCount": 1,
  "completedBefore": null,
  "deletedRunIds": []
}
```

## DELETE /api/admin/sync-runs

Bulk-delete terminal sync runs completed before a cutoff.

Query parameters:

- `completedBefore`: Required UTC timestamp.

Expected behavior:

- `200 OK` with `AdminSyncRunCleanupResultResponse`.
- `400 Bad Request` when `completedBefore` is missing or invalid.
- `400 Bad Request` when `completedBefore` is later than the current server time.
- Non-terminal runs are excluded even if their timestamps otherwise match.
- Result counts reflect committed deletion and may differ from a prior preview if history changed concurrently.

Response shape:

```json
{
  "deletedRunCount": 42,
  "deletedItemCount": 1234,
  "excludedRunCount": 1,
  "notFoundRunCount": 0,
  "completedBefore": "2026-05-16T00:00:00Z",
  "deletedRunIds": []
}
```

## Admin UI Contract

Sync Runs page behavior:

- Each terminal sync run row exposes a delete action with a confirmation step.
- Running sync runs do not expose a destructive delete action.
- Bulk cleanup control requires the administrator to choose a cutoff before previewing.
- Bulk cleanup confirmation displays eligible run count and item count from the server preview.
- After successful cleanup, sync run list queries are invalidated and refetched.
- Cleanup failures preserve the current list and show the server-provided error.
