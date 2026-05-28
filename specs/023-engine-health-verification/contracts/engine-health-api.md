# Contract: Engine Health API

All routes are scoped to a workspace and require trusted platform identity. The server derives workspace membership and deployment permissions from existing workspace authorization records.

## Common Rules

- Every route rejects anonymous callers.
- Every route rejects callers without access to `{workspaceId}`.
- Manual verification requires `deployments.setup.manage`.
- Heartbeat update currently requires the same workspace-scoped `deployments.setup.manage` authority as setup mutations until runtime-issued engine credentials are introduced.
- Responses never include raw credentials, provider tokens, secret values, raw provider errors, or stack traces.
- Cross-workspace IDs are rejected without exposing target existence.

## `POST /api/workspaces/{workspaceId}/deployments/engines/{engineId}/verify`

Manually verifies a registered workflow engine and persists safe health metadata.

Request:

```json
{}
```

Response: `200 OK`

```json
{
  "engineId": "engine-prod",
  "environmentId": "env-prod",
  "health": "Healthy",
  "version": "Elsa 4.0.1",
  "certificateStatus": "Trusted",
  "credentialVerificationStatus": "Verified",
  "credentialLastVerifiedAt": "2026-05-26T10:00:00Z",
  "lastHeartbeatAt": "2026-05-26T10:00:00Z",
  "lastVerificationAt": "2026-05-26T10:00:00Z",
  "message": "Engine verified successfully."
}
```

Failure behavior:

- Reachability failure returns `200 OK` with `health: "Unreachable"` and safe message after recording the attempt.
- Certificate or credential failure returns `200 OK` with `health: "Degraded"` and safe message after recording the attempt.
- Permission or ownership failure returns `403 Forbidden` or `404 NotFound` according to existing workspace deployment route behavior.

## `POST /api/workspaces/{workspaceId}/deployments/engines/{engineId}/heartbeat`

Accepts heartbeat metadata for a registered workflow engine.

Request:

```json
{
  "environmentId": "env-prod",
  "version": "Elsa 4.0.1",
  "certificateStatus": "Trusted",
  "credentialVerificationStatus": "Verified",
  "heartbeatAt": "2026-05-26T10:00:00Z",
  "capabilities": [
    {
      "id": "engine.reload-configuration",
      "label": "Reload engine configuration",
      "boundary": "EngineApi"
    }
  ],
  "message": "Heartbeat accepted."
}
```

Response: `200 OK`

```json
{
  "engineId": "engine-prod",
  "environmentId": "env-prod",
  "health": "Healthy",
  "version": "Elsa 4.0.1",
  "certificateStatus": "Trusted",
  "credentialVerificationStatus": "Verified",
  "credentialLastVerifiedAt": "2026-05-26T10:00:00Z",
  "lastHeartbeatAt": "2026-05-26T10:00:00Z",
  "lastVerificationAt": null,
  "message": "Heartbeat accepted."
}
```

Failure cases:

- `401 Unauthorized`: no trusted identity.
- `403 Forbidden`: caller lacks workspace setup authority.
- `404 NotFound`: engine is not visible in the workspace.
- `409 Conflict`: heartbeat is stale or environment/engine relationship does not match.
- `400 BadRequest`: heartbeat metadata is invalid.

## Cockpit Additions

Engine registration objects returned by `GET /api/workspaces/{workspaceId}/deployments/cockpit` include:

```json
{
  "health": "Healthy",
  "lastHeartbeatAt": "2026-05-26T10:00:00Z",
  "lastVerificationAt": "2026-05-26T10:00:00Z",
  "verificationMessage": "Engine verified successfully."
}
```
