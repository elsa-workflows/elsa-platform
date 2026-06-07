# Contract: Desired-State Requirements API

## List Environment Desired-State Requirements

```http
GET /api/workspaces/{workspaceId}/deployments/environments/{environmentId}/desired-state-requirements
```

Returns requirement metadata for the current environment tier.

Required permission: deployment read.

Response:

```json
{
  "environmentId": "935f8857-48db-4485-9359-ed2da1a98b25",
  "tierName": "Production",
  "tierCapabilities": [
    "deployment.tier.production-like",
    "deployment.observability.required"
  ],
  "requirements": [
    {
      "id": "observability-binding",
      "capabilityId": "deployment.observability.required",
      "recordKind": "ObservabilityBinding",
      "label": "Observability binding",
      "description": "Production targets require at least one logs, metrics, traces, or console telemetry binding.",
      "validationId": "deployment.tier.observability-required",
      "required": true,
      "applicability": "CurrentTier"
    }
  ]
}
```

Dev/Test response without additional requirements:

```json
{
  "environmentId": "935f8857-48db-4485-9359-ed2da1a98b25",
  "tierName": "Dev",
  "tierCapabilities": [
    "deployment.tier.development-like",
    "deployment.promotion.source"
  ],
  "requirements": []
}
```

Not found:

```http
404 Not Found
```

Forbidden:

```http
403 Forbidden
```

## Contextual Fix Query Parameters

The console may open a supported editor when the new revision route includes:

```text
?includeRequirement=observability-binding
```

The query parameter does not make the backend accept invalid records. It only asks the UI to display a supported editor with contextual copy.
