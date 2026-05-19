# Contract: Admin Package Details

This contract defines the admin API and UI route behavior required by the
Package Details page. It refines the broader admin dashboard UI contract for the
package details slice.

## Authentication

All endpoints are protected by the existing admin authorization boundary.

Expected access handling:

- `401` or `403`: The UI clears protected details from the current view and
  shows an access failure state.
- Protected data from a previous successful response must not be presented as
  current after access is denied.

## Admin Package Details

`GET /api/admin/packages/{packageId}`

Route behavior:

- `packageId` matching is case-insensitive.
- Successful responses return the canonical indexed package ID casing.

Response:

```json
{
  "packageId": "Elsa.Persistence.PostgreSql",
  "source": {
    "id": "00000000-0000-0000-0000-000000000001",
    "name": "Elsa Official",
    "url": "https://api.nuget.org/v3/index.json",
    "enabled": true,
    "status": "Healthy",
    "lastSyncedAt": "2026-05-15T08:00:00Z",
    "lastSuccessfulSyncAt": "2026-05-15T08:00:00Z"
  },
  "latestVersion": "1.0.2",
  "listed": true,
  "approved": false,
  "createdAt": "2026-05-15T07:00:00Z",
  "updatedAt": "2026-05-15T08:15:00Z",
  "versions": [
    {
      "version": "1.0.2",
      "approvalStatus": "Pending",
      "validationStatus": "Valid",
      "isListed": true,
      "suspiciousChangeDetected": false,
      "schemaVersion": "1.0",
      "manifestHash": "sha256:abc",
      "suspiciousManifestHash": null,
      "versionStateToken": "W/\"approval:pending-validation:valid-hash:sha256:abc\"",
      "publishedAt": "2026-05-15T06:30:00Z",
      "indexedAt": "2026-05-15T08:10:00Z",
      "featuresCount": 3,
      "settingsCount": 12,
      "compatibility": {
        "targetFrameworks": ["net10.0"],
        "elsaVersionRange": "[4.0.0,5.0.0)",
        "requiredCapabilities": ["persistence"],
        "notes": ["Requires a PostgreSQL provider configured at runtime."],
        "unsupportedCombinations": []
      },
      "visibilityReasons": [
        {
          "code": "VersionPendingApproval",
          "category": "TrustDecision",
          "severity": "Blocking",
          "message": "This package version is pending approval.",
          "blocksPublicVisibility": true
        }
      ],
      "features": [
        {
          "featureId": "postgresql",
          "typeName": "Elsa.Persistence.PostgreSql.PostgreSqlFeature",
          "displayName": "PostgreSQL Persistence",
          "description": "Stores workflow state in PostgreSQL.",
          "category": "Persistence",
          "requiredCapabilities": [],
          "dependencies": [],
          "conflicts": [],
          "infrastructure": [],
          "advanced": false,
          "experimental": false,
          "extensionsJson": "{}",
          "settings": [
            {
              "name": "connectionString",
              "displayName": "Connection string",
              "description": "Database connection string.",
              "category": "Connection",
              "jsonType": "string",
              "clrType": "System.String",
              "required": true,
              "defaultValueJson": null,
              "validationJson": "{}",
              "secret": true,
              "restartRequired": true,
              "environmentVariable": "ELSA_POSTGRESQL_CONNECTION_STRING",
              "uiJson": "{}",
              "extensionsJson": "{}"
            }
          ]
        }
      ],
      "manifest": {
        "available": true,
        "schemaVersion": "1.0",
        "manifestHash": "sha256:abc",
        "suspiciousManifestHash": null,
        "manifestJson": "{...}"
      }
    }
  ]
}
```

Expected responses:

- `200`: Package details returned.
- `401` or `403`: Admin access failure.
- `404`: Package not found or no longer accessible to the administrator.

Notes:

- If a package has no versions, `versions` is empty and the UI shows a package
  empty state.
- `versionStateToken` is an opaque freshness marker. Clients must not parse it;
  they pass it back with trust-changing actions so stale decisions can be
  rejected.
- Feature and setting JSON-backed fields may be returned either as parsed arrays
  or JSON strings, but the UI model must normalize them before display.
- The details response may include validation findings directly in each version
  if the implementation chooses to avoid a second request.

## Validation Findings

`GET /api/admin/packages/{packageId}/versions/{version}/validation`

Route behavior:

- `packageId` matching follows the package details endpoint.
- `version` must match an indexed version exactly.

Response:

```json
{
  "packageId": "Elsa.Persistence.PostgreSql",
  "version": "1.0.2",
  "findings": [
    {
      "severity": "Error",
      "code": "RequiredFieldMissing",
      "message": "Feature description is required.",
      "path": "$.features[0].description",
      "blocksPublicVisibility": true,
      "validatedAt": "2026-05-15T08:12:00Z",
      "validatorVersion": "1.0.0"
    }
  ]
}
```

Expected responses:

- `200`: Findings returned; empty `findings` means valid/no findings.
- `401` or `403`: Admin access failure.
- `404`: Package or version not found.

Notes:

- Existing JSON-encoded error and warning payloads are normalized into findings.
- Missing code or path values are allowed and must not hide the finding.
- The package details page can show a scoped validation failure if this endpoint
  fails while package details load successfully.

## Version Manifest

`GET /api/admin/packages/{packageId}/versions/{version}/manifest`

This endpoint is optional if manifest content is included in package details.

Response:

```json
{
  "packageId": "Elsa.Persistence.PostgreSql",
  "version": "1.0.2",
  "available": true,
  "schemaVersion": "1.0",
  "manifestHash": "sha256:abc",
  "suspiciousManifestHash": null,
  "manifestJson": "{...}"
}
```

Expected responses:

- `200`: Manifest metadata returned.
- `401` or `403`: Admin access failure.
- `404`: Package or version not found.

## Version Approval

`POST /api/admin/packages/{packageId}/versions/{version}/approve`

Request:

```json
{
  "reason": "Reviewed manifest and source ownership.",
  "expectedStateToken": "W/\"approval:pending-validation:valid-hash:sha256:abc\""
}
```

Rules:

- `reason` is optional for approval.
- `expectedStateToken` is required for UI-submitted approval requests.
- The UI confirmation identifies the canonical package ID and selected version.
- If the selected version changed after page load, the UI blocks submission when
  it can detect the mismatch locally; otherwise the API returns `409` when the
  submitted `expectedStateToken` no longer matches the current version state.

Expected responses:

- `204`: Approved.
- `400`: Expected state token missing or blank.
- `401` or `403`: Admin access failure.
- `404`: Package or version not found.
- `409`: Version state changed; refresh before retry.

## Version Rejection

`POST /api/admin/packages/{packageId}/versions/{version}/reject`

Request:

```json
{
  "reason": "Manifest is missing required feature descriptions.",
  "expectedStateToken": "W/\"approval:pending-validation:valid-hash:sha256:abc\""
}
```

Rules:

- `reason` is required and must contain non-whitespace text.
- `expectedStateToken` is required for UI-submitted rejection requests.
- The UI confirmation identifies the canonical package ID and selected version.
- If the selected version changed after page load, the UI blocks submission when
  it can detect the mismatch locally; otherwise the API returns `409` when the
  submitted `expectedStateToken` no longer matches the current version state.

Expected responses:

- `204`: Rejected.
- `400`: Rejection reason missing or blank.
- `401` or `403`: Admin access failure.
- `404`: Package or version not found.
- `409`: Version state changed; refresh before retry.

## Optional Version Actions

Optional actions are exposed only when supported for the selected version:

- `POST /api/admin/packages/{packageId}/versions/{version}/revalidate`
- `POST /api/admin/packages/{packageId}/versions/{version}/resync`
- `POST /api/admin/packages/{packageId}/versions/{version}/recompute-metadata`

If unsupported, the UI omits the action or shows it disabled with a reason.

## Admin UI Routes

Supported routes:

- `/admin/packages/:packageId`
- `/admin/packages/:packageId/versions/:version`
- `/admin/packages/:packageId/versions/:version/:section`

Supported section names:

- `summary`
- `validation`
- `features`
- `dependencies`
- `compatibility`
- `manifest`
- `actions`

Route rules:

- Package-level route selects the latest indexed version.
- Version route selects that version if it exists.
- Version plus section route selects that version and brings the section into
  view if it exists.
- Unknown versions show a recoverable version-not-found state.
- Unknown sections fall back to summary for the selected version.
