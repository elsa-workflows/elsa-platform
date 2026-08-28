# Contract: Runtime Artifact Download API

Runtime artifact downloads are runtime-facing and command scoped. They are separate from operator artifact downloads and require the active command lease.

## Poll And Claim Command

Runtime command polling remains under the existing command API:

```http
GET /api/workspaces/{workspaceId}/deployments/runtime/engines/{engineId}/commands?limit=10
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/claim
```

The command DTO gains an `artifacts` list for multi-artifact revision commands.

```json
{
  "command": {
    "id": "9f4f44b8-f65c-4c6f-a043-1f818c206c3d",
    "runId": "75e12435-18ec-4c69-8fb6-15d3c04e0b08",
    "revision": {
      "revisionId": "3c498114-5683-4cbf-be4e-4494a4a5f29c"
    },
    "action": "Deploy",
    "status": "Claimed",
    "artifacts": [
      {
        "artifactRecordId": "ddf89a51-0560-4945-a5d2-2f5b65b9a5ca",
        "artifactId": "dev-sample 2026.06.04.4",
        "artifactTypeId": "elsa.workflow-definition",
        "artifactSchemaVersion": "1.0",
        "contentDigest": {
          "algorithm": "sha256",
          "value": "2f5b65b9a5ca3a52cc368dabf8ed38fbb456160d1ac68f61b8dd2f8cedce81a1b"
        },
        "downloadUrl": "/api/workspaces/10000000-0000-0000-0000-000000000001/deployments/runtime/commands/9f4f44b8-f65c-4c6f-a043-1f818c206c3d/artifacts/ddf89a51-0560-4945-a5d2-2f5b65b9a5ca/download",
        "status": "Pending"
      }
    ]
  },
  "leaseToken": "opaque-lease-token"
}
```

## Download Artifact For Claimed Command

```http
GET /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/artifacts/{artifactRecordId}/download
X-Elsa-Worker-Id: runtime-sync-01
X-Elsa-Command-Lease: opaque-lease-token
```

Expected responses:

- `200 OK` with artifact stream when the lease is active and the command references the artifact.
- `403 Forbidden` when the caller is not authorized for runtime command access.
- `404 Not Found` when the command or artifact is not visible in the workspace.
- `409 Conflict` when the command is final, the lease is missing/invalid/expired, the worker does not own the lease, the command targets another engine, or the artifact is not associated with the command.

Response headers:

```http
Content-Type: application/zip
Content-Disposition: attachment; filename="dev-sample-2026.06.04.4.zip"
X-Elsa-Artifact-Digest-Algorithm: sha256
X-Elsa-Artifact-Digest: 2f5b65b9a5ca3a52cc368dabf8ed38fbb456160d1ac68f61b8dd2f8cedce81a1b
```

Runtime rules:

- The runtime verifies the downloaded bytes against the command digest before apply.
- A digest mismatch is reported as a rejected or failed artifact item with safe diagnostics.
- The download endpoint never returns raw provider tokens or local filesystem paths.

## Runtime Progress With Per-Artifact Outcomes

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/progress
Content-Type: application/json
```

```json
{
  "leaseToken": "opaque-lease-token",
  "status": "Applying",
  "percentComplete": 50,
  "message": "Applying artifact dev-sample 2026.06.04.4.",
  "artifacts": [
    {
      "artifactRecordId": "ddf89a51-0560-4945-a5d2-2f5b65b9a5ca",
      "status": "Applying"
    }
  ]
}
```

## Complete Or Fail With Per-Artifact Outcomes

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/complete
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/fail
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/reject
Content-Type: application/json
```

```json
{
  "leaseToken": "opaque-lease-token",
  "artifacts": [
    {
      "artifactRecordId": "ddf89a51-0560-4945-a5d2-2f5b65b9a5ca",
      "status": "Applied",
      "observedDigest": {
        "algorithm": "sha256",
        "value": "2f5b65b9a5ca3a52cc368dabf8ed38fbb456160d1ac68f61b8dd2f8cedce81a1b"
      },
      "runtimeReference": "elsa://workflow-definitions/dev-sample/versions/2026.06.04.4",
      "diagnostics": []
    }
  ],
  "diagnostics": []
}
```

Partial failure rules:

- If any artifact item fails after another item is applied, the aggregate run is failed or recovery-required according to finalization policy.
- Elsa Control preserves the successful and failed item outcomes.
- Elsa Control does not report automatic rollback unless a separate runtime recovery action explicitly performed and reported it.
