# Contract: Workspace Deployment Tiers API

All routes are scoped to a workspace and require customer workspace identity. The server derives account, workspace membership, workspace role, deployment permissions, and entitlements from trusted server-side records.

## Common Rules

- Every route rejects anonymous users.
- Every route rejects non-members of `{workspaceId}`.
- Tier management mutations require workspace administration authority.
- Environment assignment mutations require deployment setup permission.
- Responses never include raw secret values, engine credentials, provider tokens, or unredacted connection strings.
- Direct requests with cross-workspace tier IDs are rejected.
- Tier-aware behavior is based on coded capability IDs, not tier names.

## Coded Tier Capabilities

Initial platform-defined capability IDs:

- `deployment.tier.development-like`
- `deployment.tier.test-like`
- `deployment.tier.preproduction-like`
- `deployment.tier.production-like`
- `deployment.promotion.source`
- `deployment.promotion.target`
- `deployment.confirmation.required`
- `deployment.rollback.enabled`
- `deployment.secret-verification.required`
- `deployment.observability.required`

## `GET /api/workspaces/{workspaceId}/deployments/tier-capabilities`

Returns the platform-defined capability catalog.

Response:

```json
{
  "capabilities": [
    {
      "id": "deployment.tier.production-like",
      "label": "Production-like",
      "description": "Marks environments using this tier as production-grade deployment targets.",
      "category": "Classification",
      "isDeprecated": false
    }
  ]
}
```

## `GET /api/workspaces/{workspaceId}/deployments/tiers`

Returns workspace tier definitions with assigned capabilities.

Response:

```json
{
  "tiers": [
    {
      "id": "tier-prod-eu",
      "name": "Production EU",
      "description": "European production workflow estate",
      "sortOrder": 40,
      "isDefault": false,
      "status": "Active",
      "capabilities": [
        "deployment.tier.production-like",
        "deployment.promotion.target",
        "deployment.confirmation.required",
        "deployment.rollback.enabled",
        "deployment.secret-verification.required",
        "deployment.observability.required"
      ],
      "environmentCount": 3,
      "createdAt": "2026-05-27T09:15:00Z",
      "updatedAt": "2026-05-27T09:15:00Z"
    }
  ]
}
```

## `POST /api/workspaces/{workspaceId}/deployments/tiers`

Creates a custom deployment tier.

Request:

```json
{
  "name": "UAT",
  "description": "User acceptance testing",
  "sortOrder": 30,
  "capabilities": [
    "deployment.tier.preproduction-like",
    "deployment.promotion.source",
    "deployment.promotion.target",
    "deployment.secret-verification.required"
  ]
}
```

Response: `201 Created` with the created tier.

Errors:

- `403 Forbidden` when the caller lacks workspace administration authority.
- `409 Conflict` when another active tier in the workspace has the same name.
- `400 Bad Request` when a capability ID is unknown or deprecated for new assignment.

## `PUT /api/workspaces/{workspaceId}/deployments/tiers/{tierId}`

Updates a tier definition. Capability changes that affect assigned environments require a prior impact preview.

Request:

```json
{
  "name": "Pre-Prod",
  "description": "Final pre-production validation",
  "sortOrder": 35,
  "capabilities": [
    "deployment.tier.preproduction-like",
    "deployment.promotion.source",
    "deployment.promotion.target",
    "deployment.secret-verification.required"
  ],
  "impactAccepted": true
}
```

Response: `200 OK` with the updated tier.

Errors:

- `403 Forbidden` when the caller lacks workspace administration authority.
- `404 Not Found` when the tier is not visible in the workspace.
- `409 Conflict` when the name duplicates another active tier or impact acceptance is required.

## `POST /api/workspaces/{workspaceId}/deployments/tiers/{tierId}/impact-preview`

Returns the impact of proposed capability changes without saving them.

Request:

```json
{
  "capabilities": [
    "deployment.tier.production-like",
    "deployment.promotion.target",
    "deployment.confirmation.required"
  ]
}
```

Response:

```json
{
  "tierId": "tier-prod-eu",
  "affectedEnvironmentCount": 2,
  "affectedEnvironmentSamples": [
    {
      "applicationId": "app-claims",
      "applicationName": "Claims Operations",
      "environmentId": "env-prod-eu",
      "environmentName": "Production EU"
    }
  ],
  "addedCapabilities": [
    "deployment.confirmation.required"
  ],
  "removedCapabilities": [
    "deployment.rollback.enabled"
  ],
  "changedSafeguards": [
    "Deployments to environments using this tier will require explicit confirmation.",
    "Rollback will no longer be offered for environments using this tier."
  ]
}
```

## `POST /api/workspaces/{workspaceId}/deployments/tiers/{tierId}/archive`

Archives a tier so it cannot be selected for new or edited environments.

Response: `200 OK` with the archived tier.

Errors:

- `409 Conflict` when this is the last active tier in the workspace.

## `POST /api/workspaces/{workspaceId}/deployments/tiers/{tierId}/restore`

Restores an archived tier to active use.

Response: `200 OK` with the restored tier.

Errors:

- `409 Conflict` when another active tier has the same name.

## Cockpit Environment Shape

Environment summaries include tier identity, display label, status, and capabilities alongside the temporary legacy tier value.

```json
{
  "id": "env-prod-eu",
  "name": "Production EU",
  "tier": "Production",
  "tierId": "tier-prod-eu",
  "tierName": "Production EU",
  "tierStatus": "Active",
  "tierCapabilities": [
    "deployment.tier.production-like",
    "deployment.promotion.target",
    "deployment.confirmation.required"
  ],
  "health": "Healthy",
  "deployedRevision": 41,
  "deploymentStatus": "Succeeded",
  "driftStatus": "InSync",
  "engineIds": ["engine-prod-eu"]
}
```

## Environment Create/Update Shape

Environment create and update requests use `tierId` instead of fixed tier values.

```json
{
  "name": "Production EU",
  "tierId": "tier-prod-eu"
}
```

Compatibility during migration:

- Existing fixed tier values may remain readable in responses until all environments have tier IDs.
- New clients should send `tierId`.
- Requests with old fixed tier values should map to the equivalent default tier only during the transition period.
