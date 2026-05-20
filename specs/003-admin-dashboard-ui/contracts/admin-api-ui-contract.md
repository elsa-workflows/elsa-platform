# Admin API UI Contract

This contract documents the admin API surface the dashboard needs. It builds on
the existing Catalog Admin APIs and highlights required deltas for the clarified
MVP.

## Authentication

All requests use the existing admin authentication boundary. The UI must treat
`401` and `403` as access failures and must not present stale protected data as
current after those responses.

## Application Metadata

`GET /api/admin/application`

Returns host-owned metadata for the deployed admin application.

Required UI fields:

- `name`
- `buildNumber`

Notes:

- `buildNumber` reflects the configured deployment build number when available,
  falling back to the API assembly informational version and then assembly
  version for local development.
- The UI displays the build number in persistent dashboard chrome.
- The response is protected by the admin authentication boundary.

## Source List

`GET /api/admin/sources`

Returns active package sources by default.

Required UI fields per item:

- `id`
- `name`
- `type`
- `url`
- `enabled`
- `includePatterns`
- `excludePatterns`
- `approvalPolicy`
- `versionDiscoveryPolicy`
- `status`
- `isSyncing`
- `lastSuccessfulSyncAt`
- `lastSyncedAt`
- `packageCount`
- `createdAt`
- `updatedAt`

Notes:

- `status` and `lastSuccessfulSyncAt` are guaranteed source health fields.
- `isSyncing` is transient process state and may reflect manual or scheduled
  syncs currently running in the API host.
- `packageCount` may be zero when no packages have been indexed.
- Soft-deleted sources are omitted from the default list.

## Source Upsert

`POST /api/admin/sources`

`PUT /api/admin/sources/{id}`

Request:

```json
{
  "name": "Elsa Official",
  "url": "https://api.nuget.org/v3/index.json",
  "enabled": true,
  "includePatterns": ["Elsa.*"],
  "excludePatterns": ["*.Tests"],
  "approvalPolicy": "Manual",
  "versionDiscoveryPolicy": "LatestStable",
  "pollingInterval": "PT30M"
}
```

Response: updated source object.

Validation errors should map to fields when possible.

`versionDiscoveryPolicy` supports `AllVersions`, `LatestStable`,
`LatestIncludingPrerelease`, and `LatestPreview`. Omitted values default to
`AllVersions`.

## Source Soft-Delete

`DELETE /api/admin/sources/{id}`

MVP semantics:

- Soft-delete only.
- Hard-delete is not exposed.
- Source leaves default active source list after success.
- Historical package, validation, and sync records may remain.

Expected responses:

- `204`: soft-deleted.
- `404`: source missing or already inaccessible.
- `409`: source changed and should be refreshed before retry.

## Source Sync

`POST /api/admin/sync/sources/{sourceId}`

Triggers a manual source sync.

Response should include a sync run or sync run ID so the UI can link to Sync Run
Details.

## Pattern Tester

The UI may run pattern tests locally using documented glob semantics:

- Case-insensitive.
- `*` matches any text.
- `?` matches one character.
- Excludes take precedence.

If a backend tester endpoint is added later, it should return:

```json
{
  "items": [
    {
      "packageId": "Elsa.Persistence.PostgreSql",
      "included": true,
      "matchedIncludePatterns": ["Elsa.*"],
      "matchedExcludePatterns": []
    }
  ]
}
```

## Package List

`GET /api/admin/packages`

Query parameters should support, when implemented:

- `q`
- `approvalStatus`
- `validationStatus`
- `sourceId`
- `listed`
- `suspicious`
- `sort`
- `page`
- `pageSize`

Required UI fields:

- `packageId`
- `sourceId`
- `latestVersion`
- `approvalStatus` summary for display only
- `validationStatus` summary
- `listed`
- `featuresCount`
- `updatedAt`
- `versions`

Trust-changing actions are not performed on package identity rows in the MVP.

## Package Details

`GET /api/admin/packages/{packageId}`

Required UI fields:

- Package identity and source.
- Available versions.
- Per-version approval, validation, listing, suspicious, schema, and hash state.
- Feature metadata for selected version when available.
- Visibility explanation inputs.

## Package Version Manifest

Required for Manifest Viewer. Endpoint shape may be either included in package
details or exposed separately:

`GET /api/admin/packages/{packageId}/versions/{version}/manifest`

Response:

```json
{
  "packageId": "Elsa.Persistence.PostgreSql",
  "version": "1.0.2",
  "schemaVersion": "1",
  "manifestHash": "sha256:...",
  "manifestJson": "{...}"
}
```

## Validation Results

`GET /api/admin/packages/{packageId}/versions/{version}/validation`

Returns errors and warnings for the selected package version. If the current API
stores JSON-encoded error and warning payloads, the UI adapter normalizes them
to finding arrays with:

- `severity`
- `code` or `ruleId`
- `message`
- `path` or `fieldPath`

## Version Approval

`POST /api/admin/packages/{packageId}/versions/{version}/approve`

Request body may include optional reason for audit, but the UI does not require
one for approval:

```json
{
  "reason": "Reviewed manifest and source ownership."
}
```

Package identity approval endpoints must not be surfaced in the MVP UI.

## Version Rejection

`POST /api/admin/packages/{packageId}/versions/{version}/reject`

Request body:

```json
{
  "reason": "Manifest is missing required feature descriptions."
}
```

UI rule:

- Reason is required before submission.

Backend expectation:

- Reject empty or whitespace reason for all package version rejection requests.

## Version Operational Actions

These package-version actions are optional until the backend supports them. When
exposed, the UI must call version-scoped endpoints only:

- `POST /api/admin/packages/{packageId}/versions/{version}/resync`
- `POST /api/admin/packages/{packageId}/versions/{version}/revalidate`
- `POST /api/admin/packages/{packageId}/versions/{version}/recompute-metadata`

If an action is not supported by the admin API, the UI must omit or disable that
action and explain that it is unavailable.

## Bulk Version Actions

The first UI can implement bulk actions as repeated per-version requests if no
bulk endpoint exists. The UI must report item-level success and failure.

Future bulk endpoint shape:

```json
{
  "items": [
    { "packageId": "Elsa.Persistence.PostgreSql", "version": "1.0.2" }
  ],
  "reason": "Approved after review."
}
```

## Sync Runs

`GET /api/admin/sync-runs`

`GET /api/admin/sync-runs/{id}`

`POST /api/admin/sync-runs/{id}/cancel`

Required UI fields:

- `id`
- `trigger`
- `status`
- `startedAt`
- `completedAt`
- `duration`
- `error`
- `summaryCounters`
- `itemCount`
- `sources`
- `items`

Cancel response:

- `200`: cancellation was requested and the response includes the current sync
  run representation. The run may still be `Running` until in-flight work
  observes cancellation.
- `404`: sync run does not exist.
- `409`: sync run is not currently cancelable, either because it is already
  terminal or because this API host no longer owns the in-process run.

Sync run source fields:

- `id`
- `name`

Sync run item fields:

- `id`
- `sourceId`
- `packageId`
- `version`
- `status`
- `message`
- `error`
- `startedAt`
- `completedAt`

## Error Response Normalization

The UI adapter normalizes failures into:

- `Unauthorized`
- `Forbidden`
- `Validation`
- `Conflict`
- `NotFound`
- `Unavailable`
- `Unexpected`

Every mutation result exposed to screens must include enough information to show
pending, success, failure, or partial failure.
