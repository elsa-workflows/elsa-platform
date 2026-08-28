# Contract: Artifact Envelope And Types

## Envelope Submission

```http
POST /api/workspaces/{workspaceId}/artifacts
Content-Type: application/json
```

```json
{
  "artifactId": "sales-onboarding:2026.05.28.1",
  "envelopeVersion": "elsa-control/artifact-envelope/v1alpha1",
  "artifactTypeId": "elsa.workflow-definition",
  "artifactSchemaVersion": "1.0",
  "contentDigest": {
    "algorithm": "sha256",
    "value": "0f4b3d4cf7f7c4d9a3c0d9a829c22d5e6fbf9871bcf14d8d04f1d7e0ee5f4a12"
  },
  "manifestDigest": {
    "algorithm": "sha256",
    "value": "54e2a39b5b54f7b0f7d8db2f4e2a0f3bc470933027f22154bf9a7f39192e2f10"
  },
  "payloadReference": {
    "provider": "producer-managed",
    "uri": "studio://workspace/acme/workflows/sales-onboarding/versions/2026.05.28.1",
    "mediaType": "application/vnd.elsa.workflow-definition+json",
    "sizeBytes": 18442
  },
  "producer": {
    "producerType": "studio",
    "producerName": "Elsa Studio",
    "producerVersion": "4.0.0-preview",
    "sourceReference": "workflow-definition:sales-onboarding"
  },
  "displayMetadata": {
    "name": "Sales Onboarding",
    "version": "2026.05.28.1",
    "description": "Workflow submitted from Studio for platform promotion.",
    "labels": {
      "domain": "sales",
      "owner": "revops"
    },
    "annotations": {
      "studio.workflowId": "sales-onboarding"
    }
  },
  "compatibilityHints": [
    {
      "requiredArtifactType": "elsa.workflow-definition",
      "runtimeFamily": "elsa-workflows",
      "runtimeVersionRange": ">=4.0.0",
      "requiredCapabilities": [
        "workflow-definition.apply"
      ]
    }
  ],
  "diagnostics": []
}
```

Expected responses:

- `201 Created` when a new envelope-backed artifact is registered.
- `200 OK` when an identical envelope submission is idempotent.
- `400 Bad Request` for invalid digest shape, malformed metadata, unsafe fields, or unknown artifact type.
- `403 Forbidden` when the caller lacks workspace setup permission.
- `409 Conflict` when the same artifact identity exists with different immutable metadata.

## Artifact Type Discovery

```http
GET /api/workspaces/{workspaceId}/artifacts/types
```

```json
{
  "items": [
    {
      "typeId": "elsa.workflow-definition",
      "displayName": "Elsa Workflow Definition",
      "ownedBy": "platform",
      "enabled": true,
      "supportedSchemaVersions": [
        "1.0"
      ],
      "defaultRuntimeFamily": "elsa-workflows"
    }
  ]
}
```

## Artifact List Projection

```http
GET /api/workspaces/{workspaceId}/artifacts
```

```json
{
  "items": [
    {
      "id": "37f8614d-91e4-48e5-b70c-9aab53030095",
      "artifactId": "sales-onboarding:2026.05.28.1",
      "artifactTypeId": "elsa.workflow-definition",
      "artifactSchemaVersion": "1.0",
      "producerType": "studio",
      "producerName": "Elsa Studio",
      "displayName": "Sales Onboarding",
      "displayVersion": "2026.05.28.1",
      "contentDigest": {
        "algorithm": "sha256",
        "value": "0f4b3d4cf7f7c4d9a3c0d9a829c22d5e6fbf9871bcf14d8d04f1d7e0ee5f4a12"
      },
      "compatibilitySummary": {
        "runtimeFamilies": [
          "elsa-workflows"
        ],
        "requiredCapabilities": [
          "workflow-definition.apply"
        ]
      },
      "inspectionStatus": "valid",
      "submittedAt": "2026-05-28T11:00:00Z"
    }
  ]
}
```

## Safety Rules

- Request and response bodies must not contain raw workflow definition JSON, manifest JSON, credentials, bearer tokens, connection strings, or webhook secrets.
- Secret-like metadata keys such as `password`, `secret`, `token`, `authorization`, `connectionString`, and equivalent casing variants are rejected or redacted.
- Diagnostics must use stable codes and safe messages only.
