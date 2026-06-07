# Contract: Deployability API

Deployability APIs are operator-facing and workspace scoped. They require the caller to resolve to the workspace and have deployment read permission. Queueing still requires deployment execution permission and confirmation when applicable.

## Evaluate Revision Deployability

```http
POST /api/workspaces/{workspaceId}/deployments/revisions/{revisionId}/deployability
Content-Type: application/json
```

```json
{
  "targetEnvironmentId": "935f8857-48db-4485-9359-ed2da1a98b25",
  "targetEngineId": "fbfd551b-6997-4b51-9842-9c0a35d5d2d2",
  "mode": "Apply"
}
```

```json
{
  "workspaceId": "10000000-0000-0000-0000-000000000001",
  "revisionId": "3c498114-5683-4cbf-be4e-4494a4a5f29c",
  "environmentId": "935f8857-48db-4485-9359-ed2da1a98b25",
  "targetEngineId": "fbfd551b-6997-4b51-9842-9c0a35d5d2d2",
  "mode": "Apply",
  "status": "Deployable",
  "evaluatedAt": "2026-06-07T12:00:00Z",
  "artifacts": [
    {
      "artifactRecordId": "ddf89a51-0560-4945-a5d2-2f5b65b9a5ca",
      "recordName": "dev-sample 2026.06.04.4",
      "artifactId": "dev-sample 2026.06.04.4",
      "artifactTypeId": "elsa.workflow-definition",
      "artifactSchemaVersion": "1.0",
      "contentDigest": {
        "algorithm": "sha256",
        "value": "2f5b65b9a5ca3a52cc368dabf8ed38fbb456160d1ac68f61b8dd2f8cedce81a1b"
      },
      "requiredCapabilities": ["artifact.elsa.workflow-definition.apply"],
      "missingCapabilities": [],
      "payloadAvailable": true,
      "status": "Deployable",
      "diagnostics": []
    }
  ],
  "blockers": []
}
```

Blocked response shape:

```json
{
  "status": "Blocked",
  "artifacts": [
    {
      "artifactRecordId": "ddf89a51-0560-4945-a5d2-2f5b65b9a5ca",
      "recordName": "dev-sample 2026.06.04.4",
      "artifactTypeId": "elsa.workflow-definition",
      "requiredCapabilities": ["artifact.elsa.workflow-definition.apply"],
      "missingCapabilities": ["artifact.elsa.workflow-definition.apply"],
      "payloadAvailable": true,
      "status": "Blocked",
      "diagnostics": [
        {
          "id": "artifact.capability.missing",
          "severity": "Blocker",
          "scope": "EngineCapabilities",
          "message": "dev-sample 2026.06.04.4 requires runtime capability artifact.elsa.workflow-definition.apply."
        }
      ]
    }
  ],
  "blockers": [
    {
      "id": "artifact.capability.missing",
      "severity": "Blocker",
      "scope": "EngineCapabilities",
      "message": "dev-sample 2026.06.04.4 requires runtime capability artifact.elsa.workflow-definition.apply.",
      "remediation": "Refresh the engine heartbeat or install the workflow definition runtime applier.",
      "artifactRecordId": "ddf89a51-0560-4945-a5d2-2f5b65b9a5ca",
      "engineId": "fbfd551b-6997-4b51-9842-9c0a35d5d2d2"
    }
  ]
}
```

Expected responses:

- `200 OK` when evaluation completes, even if the result is blocked.
- `400 Bad Request` for invalid mode or malformed request.
- `403 Forbidden` when the caller lacks deployment read permission.
- `404 Not Found` when the revision is not visible in the workspace.
- Missing target environments, missing target engines, target-engine environment mismatches, and unsafe revision prerequisites are returned as structured blocked deployability results so the console can show remediation without a second error path.

## Queue Deployment Run

Existing queue endpoints continue to create deployment runs, but they must call the same deployability service before persistence.

```http
POST /api/workspaces/{workspaceId}/deployments/runs
Content-Type: application/json
```

```json
{
  "sourceRevisionId": "3c498114-5683-4cbf-be4e-4494a4a5f29c",
  "targetEnvironmentId": "935f8857-48db-4485-9359-ed2da1a98b25",
  "targetEngineId": "fbfd551b-6997-4b51-9842-9c0a35d5d2d2",
  "confirmationId": "11fb5b18-d22a-45fb-9a55-f62bb69e93f7",
  "mode": "Apply"
}
```

Queueing rules:

- Permission, confirmation, tier, idempotency, and deployability checks all pass before any command is created.
- A blocked deployability result returns `409 Conflict` with safe blocker details.
- Duplicate approved queue requests for the same revision, target engine, and mode return the existing run/command state or a deterministic conflict, but never create duplicate runtime apply side effects.

## Engine Capability Contract

Registered engines and heartbeat updates must advertise canonical artifact apply capabilities.

```json
{
  "capabilities": [
    {
      "id": "artifact.elsa.workflow-definition.apply",
      "label": "Apply Elsa workflow definition artifacts",
      "boundary": "Workflow",
      "artifactTypeId": "elsa.workflow-definition",
      "supportedSchemaVersions": ["1.0"]
    }
  ]
}
```

Compatibility rules:

- New runtime integrations must advertise canonical capability IDs.
- Legacy short capability IDs may be accepted as aliases during migration but must be shown to users as missing canonical IDs when the canonical operation is not actually supported.
- Stale or missing capability heartbeat metadata blocks deployment before queueing.
