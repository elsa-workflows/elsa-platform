# Contract: Workspace Artifact Registry API

All routes are scoped to a workspace and use the existing workspace identity and deployment permission model.

## Common Rules

- Reads require workspace access plus `deployments.read`.
- Registration and inspection refresh require workspace access plus `deployments.setup.manage`.
- Upload session creation, upload completion, and upload abort require workspace access plus `deployments.setup.manage`.
- Responses never include raw artifact payloads, manifest JSON, workflow definitions, provider tokens, raw credentials, secret values, or stack traces.
- Artifact IDs from other workspaces return not found or forbidden according to existing workspace route behavior.

## `GET /api/workspaces/{workspaceId}/artifacts`

Lists registered artifact metadata for the workspace.

Response: `200 OK`

```json
{
  "items": [
    {
      "id": "artifact-record-id",
      "artifactId": "sha256:abc123",
      "layoutVersion": "platform.elsa.io/deployment-artifact/v1alpha1",
      "contentDigest": { "algorithm": "sha256", "value": "abc123" },
      "format": "Zip",
      "referenceProvider": "local",
      "manifestName": "claims",
      "manifestVersion": "1.0.0",
      "manifestEnvironment": "prod",
      "resourceCount": 3,
      "checksumStatus": "Verified",
      "inspectionStatus": "Valid",
      "registeredAt": "2026-05-26T10:00:00Z",
      "lastInspectedAt": "2026-05-26T10:05:00Z"
    }
  ]
}
```

## `GET /api/workspaces/{workspaceId}/artifacts/{artifactRecordId}`

Returns one artifact record with safe resource summaries and diagnostics.

Response: `200 OK`

```json
{
  "id": "artifact-record-id",
  "artifactId": "sha256:abc123",
  "layoutVersion": "platform.elsa.io/deployment-artifact/v1alpha1",
  "contentDigest": { "algorithm": "sha256", "value": "abc123" },
  "format": "Zip",
  "referenceProvider": "local",
  "reference": "local://artifacts/claims-prod.zip",
  "manifest": {
    "name": "claims",
    "version": "1.0.0",
    "environment": "prod"
  },
  "resources": [
    {
      "type": "workflowDefinition",
      "logicalId": "payment-retry",
      "scope": null,
      "version": "8",
      "desiredStateHash": { "algorithm": "sha256", "value": "def456" }
    }
  ],
  "checksumStatus": "Verified",
  "inspectionStatus": "Valid",
  "diagnostics": [],
  "registeredAt": "2026-05-26T10:00:00Z",
  "lastInspectedAt": "2026-05-26T10:05:00Z"
}
```

## `POST /api/workspaces/{workspaceId}/artifacts`

Registers metadata for an already-built artifact.

Request:

```json
{
  "artifactId": "sha256:abc123",
  "layoutVersion": "platform.elsa.io/deployment-artifact/v1alpha1",
  "contentDigest": { "algorithm": "sha256", "value": "abc123" },
  "format": "Zip",
  "referenceProvider": "local",
  "reference": "local://artifacts/claims-prod.zip",
  "manifest": {
    "name": "claims",
    "version": "1.0.0",
    "environment": "prod"
  },
  "resources": [
    {
      "type": "workflowDefinition",
      "logicalId": "payment-retry",
      "scope": null,
      "version": "8",
      "desiredStateHash": { "algorithm": "sha256", "value": "def456" }
    }
  ],
  "diagnostics": []
}
```

Response:

- `201 Created` with artifact detail when a new record is created.
- `200 OK` with artifact detail when an identical idempotent record already exists.
- `409 Conflict` when the artifact identity already exists with conflicting metadata.

## `POST /api/workspaces/{workspaceId}/artifacts/{artifactRecordId}/refresh`

Refreshes inspection state from the registered reference when the reference provider is supported.

Response: `200 OK`

```json
{
  "artifactId": "sha256:abc123",
  "checksumStatus": "Verified",
  "inspectionStatus": "Valid",
  "lastInspectedAt": "2026-05-26T10:05:00Z",
  "resourceCount": 3,
  "diagnostics": []
}
```

Failure behavior:

- Unsupported references return `409 Conflict` and keep the prior record unchanged or mark inspection unsupported.
- Missing or mismatched referenced artifacts return `200 OK` with `inspectionStatus: "Invalid"` and safe diagnostics.

## Follow-up Upload API

The upload API is a follow-up slice that creates the same artifact registry records as `POST /artifacts`, but derives artifact identity and metadata from uploaded bytes. It requires a configured artifact storage provider.

### `POST /api/workspaces/{workspaceId}/artifact-uploads`

Creates an upload session for a ZIP artifact payload.

Request:

```json
{
  "fileName": "claims-prod.zip",
  "contentType": "application/zip",
  "sizeBytes": 1048576,
  "expectedDigest": { "algorithm": "sha256", "value": "abc123" },
  "idempotencyKey": "upload-claims-prod-2026-06-04"
}
```

Response: `201 Created`

```json
{
  "uploadId": "upload-session-id",
  "status": "Pending",
  "expiresAt": "2026-06-04T12:30:00Z",
  "maxSizeBytes": 52428800,
  "upload": {
    "mode": "ApiStream",
    "method": "PUT",
    "url": "/api/workspaces/workspace-id/artifact-uploads/upload-session-id/content"
  }
}
```

Notes:

- `upload.mode` may be `ApiStream` for local/dev or `ProviderDirect` for direct-to-object-storage implementations.
- Provider-direct responses must not expose long-lived credentials. Short-lived signed URLs are allowed only when scoped to the staged object, method, size, content type, and expiration.
- Reusing the same idempotency key with matching request metadata returns the existing pending or completed session.

### `PUT /api/workspaces/{workspaceId}/artifact-uploads/{uploadId}/content`

Streams bytes to the platform API when the session uses `ApiStream` mode.

Response:

- `204 No Content` when the staged object is written.
- `413 Payload Too Large` when size limits are exceeded.
- `409 Conflict` when the session is expired, completed, aborted, or belongs to another workspace.

### `POST /api/workspaces/{workspaceId}/artifact-uploads/{uploadId}/complete`

Completes an upload by verifying staged bytes, computing digest, inspecting the artifact envelope/manifest, extracting safe resource summaries, and creating or returning the artifact record.

Response: `201 Created` or `200 OK`

```json
{
  "uploadId": "upload-session-id",
  "status": "Completed",
  "artifact": {
    "id": "artifact-record-id",
    "artifactId": "sha256:abc123",
    "layoutVersion": "platform.elsa.io/deployment-artifact/v1alpha1",
    "contentDigest": { "algorithm": "sha256", "value": "abc123" },
    "format": "Zip",
    "referenceProvider": "artifact-store",
    "manifestName": "claims",
    "manifestVersion": "1.0.0",
    "resourceCount": 3,
    "checksumStatus": "Verified",
    "inspectionStatus": "Valid"
  },
  "diagnostics": []
}
```

Failure behavior:

- Unsupported layout, missing manifest, unsafe ZIP paths, decompression limits, digest mismatch, failed scan, or malformed artifact content returns a failed upload result with safe diagnostics and no deployable artifact record.
- Duplicate artifact content returns the existing artifact record idempotently or a duplicate response; it must not store a second payload copy.
- Failed uploads must delete, quarantine, or mark staged payloads cleanup pending according to storage provider capabilities.

### `DELETE /api/workspaces/{workspaceId}/artifact-uploads/{uploadId}`

Aborts a pending upload session and deletes or marks staged bytes cleanup pending.

Response:

- `204 No Content` when the session is aborted or already expired.
- `409 Conflict` when the session is already completed.
