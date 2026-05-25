# Contract: Workspace Deployment API

All routes are scoped to a workspace and require customer workspace identity unless explicitly noted. The server derives account, workspace permission grants, and entitlements from trusted identity and server records.

## Common Rules

- Every route rejects anonymous users.
- Every route rejects non-members of `{workspaceId}`.
- Mutation routes require the operation-specific permission grant and deployment entitlement.
- Responses never include raw secret values, engine API credentials, provider tokens, or unredacted connection strings.
- Direct requests with cross-workspace IDs are rejected even when the caller knows the ID.
- Deployment, rollback, and runtime control execution require a single-use confirmation created by the same account.
- Deployment and rollback requests enqueue durable runs; completion is observed by polling run/cockpit routes.

## Permissions

Deployment APIs use these permission IDs:

- `deployments.read`
- `deployments.setup.manage`
- `deployments.desired-state.manage`
- `deployments.promotion.preview`
- `deployments.run.execute`
- `deployments.rollback.execute`
- `deployments.controls.execute`
- `deployments.observability.manage`

## `GET /api/workspaces/{workspaceId}/deployments/permissions`

Returns the caller's effective deployment permissions for the workspace.

Response:

```json
{
  "permissions": [
    "deployments.read",
    "deployments.setup.manage",
    "deployments.promotion.preview"
  ]
}
```

## `GET /api/workspaces/{workspaceId}/deployments/cockpit`

Returns the current workspace deployment cockpit.

Response:

```json
{
  "applications": [
    {
      "id": "app-claims",
      "name": "Claims Operations",
      "workspaceName": "Acme Platform",
      "environments": [
        {
          "id": "env-prod",
          "name": "Prod",
          "tier": "Production",
          "health": "Unreachable",
          "desiredRevision": {
            "id": "rev-prod-40",
            "revision": 40,
            "commit": "11ec9d4",
            "label": "Baseline production",
            "authoredAt": "2026-05-19T15:45:00Z"
          },
          "deployedRevision": 40,
          "deploymentStatus": "Blocked",
          "driftStatus": "Unknown",
          "engineIds": ["engine-prod"]
        }
      ]
    }
  ],
  "engines": [
    {
      "id": "engine-prod",
      "name": "claims-prod-weu-01",
      "environmentId": "env-prod",
      "endpoint": {
        "baseUrl": "https://workflows.example.test/elsa",
        "region": "westeurope",
        "version": "Elsa 4.0.0",
        "certificateStatus": "Trusted"
      },
      "credentialReference": {
        "provider": "Azure Key Vault",
        "reference": "kv://acme-platform/prod/elsa-api",
        "verificationStatus": "Verified",
        "lastVerifiedAt": "2026-05-22T08:16:30Z"
      },
      "health": "Healthy",
      "lastHeartbeatAt": "2026-05-22T08:16:30Z",
      "capabilities": [
        {
          "id": "engine.reload-configuration",
          "label": "Reload engine configuration",
          "boundary": "EngineApi"
        }
      ],
      "controls": [
        {
          "id": "reload-configuration",
          "label": "Reload Configuration",
          "boundary": "EngineApi",
          "capabilityId": "engine.reload-configuration",
          "description": "Reloads engine API configuration from desired state."
        }
      ],
      "hostingProvider": null
    }
  ],
  "comparisons": [],
  "observabilityBindings": [
    {
      "id": "obs-prod-logs",
      "environmentId": "env-prod",
      "engineId": "engine-prod",
      "kind": "Logs",
      "provider": "Azure Monitor",
      "status": "Connected",
      "scope": "workspace:/subscriptions/.../resourceGroups/acme-prod",
      "correlatedRevisionId": "rev-prod-40",
      "sample": "Last status imported at 2026-05-25T09:30:00Z"
    }
  ],
  "history": [],
  "driftReport": [
    {
      "id": "drift-prod-001",
      "environmentId": "env-prod",
      "engineId": "engine-prod",
      "area": "RuntimeConfiguration",
      "desired": "Concurrency limit 32",
      "observed": "Concurrency limit 16",
      "action": "Review",
      "detectedAt": "2026-05-25T09:30:00Z"
    }
  ],
  "assistantPlans": []
}
```

## `POST /api/workspaces/{workspaceId}/deployments/applications`

Creates a workflow application.

Request:

```json
{
  "name": "Claims Operations",
  "description": "Claims workflow estate"
}
```

Response: `201 Created` with the created application summary.

## `POST /api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments`

Creates a deployment environment.

Request:

```json
{
  "name": "Prod",
  "tier": "Production"
}
```

Response: `201 Created` with the created environment summary.

## `POST /api/workspaces/{workspaceId}/deployments/environments/{environmentId}/engines`

Registers a workflow engine.

Request:

```json
{
  "name": "claims-prod-weu-01",
  "baseUrl": "https://workflows.example.test/elsa",
  "region": "westeurope",
  "credentialProvider": "Azure Key Vault",
  "credentialReference": "kv://acme-platform/prod/elsa-api",
  "capabilities": [
    {
      "id": "engine.reload-configuration",
      "label": "Reload engine configuration",
      "boundary": "EngineApi"
    }
  ],
  "controls": [
    {
      "id": "reload-configuration",
      "label": "Reload Configuration",
      "boundary": "EngineApi",
      "capabilityId": "engine.reload-configuration",
      "description": "Reloads engine API configuration from desired state."
    }
  ],
  "hostingProvider": null
}
```

Response: `201 Created` with the engine registration.

## `POST /api/workspaces/{workspaceId}/deployments/environments/{environmentId}/revisions`

Creates an immutable desired-state revision from structured platform records.

Request:

```json
{
  "label": "Payment retry workflow",
  "commit": "8f6a9c1",
  "records": [
    {
      "kind": "Workflow",
      "name": "Payment Retry",
      "payload": {}
    },
    {
      "kind": "SecretReference",
      "name": "Payment API",
      "payload": {
        "provider": "Azure Key Vault",
        "reference": "kv://acme-platform/prod/payment-api"
      }
    }
  ]
}
```

Response: `201 Created` with revision metadata.

## `POST /api/workspaces/{workspaceId}/deployments/promotions/preview`

Compares revisions and validates deployment readiness.

Request:

```json
{
  "sourceEnvironmentId": "env-stage",
  "targetEnvironmentId": "env-prod",
  "sourceRevisionId": "rev-stage-41",
  "targetEngineId": "engine-prod"
}
```

Response:

```json
{
  "sourceEnvironmentId": "env-stage",
  "targetEnvironmentId": "env-prod",
  "sourceRevision": 41,
  "targetRevision": 40,
  "diff": [
    {
      "id": "workflow-payment-retry",
      "category": "Workflows",
      "name": "Payment Retry",
      "sourceValue": "v7 with idempotent retry",
      "targetValue": "v6",
      "impact": "Changed"
    }
  ],
  "validations": [
    {
      "id": "secret-payment-api",
      "severity": "Blocker",
      "scope": "Secret references",
      "message": "Payment API secret reference is missing or not verified in Prod.",
      "code": "deployment.secret.missing"
    }
  ],
  "rollbackRevision": 39,
  "canDeploy": false
}
```

## `POST /api/workspaces/{workspaceId}/deployments/runs`

Enqueues a deployment run from a source revision to a target environment and engine.

Request:

```json
{
  "sourceRevisionId": "rev-stage-41",
  "targetEnvironmentId": "env-prod",
  "targetEngineId": "engine-prod",
  "confirmationId": "confirm-123",
  "mode": "Apply"
}
```

Response: `202 Accepted` with deployment run summary. `RecoveryRequired` is a terminal review state for stale claimed runs and is not automatically replayed.

```json
{
  "id": "run-410",
  "status": "Queued",
  "queuedAt": "2026-05-25T10:15:00Z"
}
```

## `POST /api/workspaces/{workspaceId}/deployments/runs/{runId}/rollback`

Enqueues a rollback deployment run from a prior compatible successful run.

Request:

```json
{
  "targetEngineId": "engine-prod",
  "confirmationId": "confirm-456"
}
```

Response: `202 Accepted` with rollback run summary.

## `GET /api/workspaces/{workspaceId}/deployments/runs/{runId}`

Returns a deployment run and validation/history details.

## `POST /api/workspaces/{workspaceId}/deployments/confirmations`

Creates a single-use confirmation for a risky action.

Request:

```json
{
  "actionType": "Deploy",
  "targetId": "env-prod",
  "summary": "Deploy revision 41 to Prod"
}
```

Response: `201 Created` with confirmation metadata. The confirmation can only be consumed once, before expiry, by the same account that created it, and only for the requested workspace/action/target tuple.

## `POST /api/workspaces/{workspaceId}/deployments/engines/{engineId}/controls/{controlId}`

Executes a supported runtime control.

Request:

```json
{
  "reason": "Reload after approved configuration deployment",
  "confirmationId": "confirm-789"
}
```

Response: `202 Accepted` with action audit summary.

Failure cases:

- `401 Unauthorized`: no customer identity.
- `403 Forbidden`: not a workspace member or insufficient permission grant/entitlement.
- `404 NotFound`: workspace-owned record is not visible to caller.
- `409 Conflict`: active deployment run conflict or immutable revision conflict.
- `422 UnprocessableEntity`: validation blockers, missing confirmation, unsupported capability, unsafe secret state, or unreachable engine.
