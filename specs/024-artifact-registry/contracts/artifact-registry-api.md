# Contract: Workspace Artifact Registry API

All routes are scoped to a workspace and use the existing workspace identity and deployment permission model.

## Common Rules

- Reads require workspace access plus `deployments.read`.
- Registration and inspection refresh require workspace access plus `deployments.setup.manage`.
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
