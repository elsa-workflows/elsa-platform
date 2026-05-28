# Contract: Runtime Command API

Runtime command APIs are runtime-facing and workspace/engine scoped. Exact authentication can evolve, but every request must resolve to a caller authorized for the workspace and target engine.

## Poll Pending Commands

```http
GET /api/workspaces/{workspaceId}/deployments/runtime/engines/{engineId}/commands?limit=10
```

```json
{
  "commands": [
    {
      "id": "9f4f44b8-f65c-4c6f-a043-1f818c206c3d",
      "runId": "75e12435-18ec-4c69-8fb6-15d3c04e0b08",
      "action": "deploy",
      "status": "pending",
      "idempotencyKey": "workspace:engine:revision:artifact",
      "artifact": {
        "artifactRecordId": "37f8614d-91e4-48e5-b70c-9aab53030095",
        "artifactId": "sales-onboarding:2026.05.28.1",
        "artifactTypeId": "elsa.workflow-definition",
        "contentDigest": {
          "algorithm": "sha256",
          "value": "0f4b3d4cf7f7c4d9a3c0d9a829c22d5e6fbf9871bcf14d8d04f1d7e0ee5f4a12"
        }
      },
      "availableAt": "2026-05-28T12:00:00Z",
      "expiresAt": "2026-05-28T13:00:00Z"
    }
  ]
}
```

## Claim Command

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/claim
Content-Type: application/json
```

```json
{
  "engineId": "3c8d5b87-b7d7-42e8-9b4c-17081e39c9a9",
  "workerId": "runtime-sync-01",
  "leaseSeconds": 120
}
```

```json
{
  "command": {
    "id": "9f4f44b8-f65c-4c6f-a043-1f818c206c3d",
    "status": "claimed",
    "leaseExpiresAt": "2026-05-28T12:02:00Z",
    "attemptNumber": 1
  },
  "leaseToken": "opaque-lease-token",
  "payload": "The command body contains safe artifact/revision references only. Raw payloads and lease tokens are not returned by poll responses."
}
```

Expected responses:

- `200 OK` when the claim succeeds.
- `404 Not Found` when the command is not visible to the runtime.
- `409 Conflict` when the command is already leased or final.
- `403 Forbidden` when the caller is not authorized for the workspace/engine.

## Heartbeat

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/heartbeat
Content-Type: application/json
```

```json
{
  "leaseToken": "opaque-lease-token",
  "workerId": "runtime-sync-01"
}
```

## Progress

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/progress
Content-Type: application/json
```

```json
{
  "leaseToken": "opaque-lease-token",
  "status": "applying",
  "percentComplete": 50,
  "message": "Applying workflow definition artifact."
}
```

## Complete

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/complete
Content-Type: application/json
```

```json
{
  "leaseToken": "opaque-lease-token",
  "observedArtifactDigest": {
    "algorithm": "sha256",
    "value": "0f4b3d4cf7f7c4d9a3c0d9a829c22d5e6fbf9871bcf14d8d04f1d7e0ee5f4a12"
  },
  "runtimeReference": "elsa://workflow-definitions/sales-onboarding/versions/2026.05.28.1",
  "diagnostics": []
}
```

## Fail Or Reject

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/fail
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/reject
```

Both accept safe diagnostics and require the current lease token. Fail means the runtime attempted processing and encountered an error. Reject means the runtime refused the command before apply, such as unsupported artifact type or incompatible schema.

## Webhook Trigger

Webhook payloads are intentionally small:

```http
POST /api/workspaces/{workspaceId}/deployments/runtime/commands/{commandId}/webhook-notifications
Content-Type: application/json
```

```json
{
  "engineId": "3c8d5b87-b7d7-42e8-9b4c-17081e39c9a9"
}
```

```json
{
  "id": "31c3faca-b812-4e5b-95cb-1b17c66be8a6",
  "workspaceId": "10000000-0000-0000-0000-000000000001",
  "engineId": "3c8d5b87-b7d7-42e8-9b4c-17081e39c9a9",
  "commandHint": "9f4f44b8-f65c-4c6f-a043-1f818c206c3d",
  "status": "pending",
  "safePayloadJson": "{\"workspaceId\":\"10000000-0000-0000-0000-000000000001\",\"engineId\":\"3c8d5b87-b7d7-42e8-9b4c-17081e39c9a9\",\"commandHint\":\"9f4f44b8-f65c-4c6f-a043-1f818c206c3d\",\"reason\":\"command-available\"}",
  "createdAt": "2026-05-28T12:00:00Z",
  "sentAt": null
}
```

The serialized webhook payload remains a safe command-available hint:

```json
{
  "workspaceId": "10000000-0000-0000-0000-000000000001",
  "engineId": "3c8d5b87-b7d7-42e8-9b4c-17081e39c9a9",
  "commandHint": "9f4f44b8-f65c-4c6f-a043-1f818c206c3d",
  "reason": "command-available"
}
```

The runtime must call poll/claim before acting.
