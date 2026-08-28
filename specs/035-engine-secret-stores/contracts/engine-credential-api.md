# Contract: Engine Credential Workspace API

All routes remain under `/api/workspaces/{workspaceId}/deployments` and use existing workspace access plus deployment setup permissions.

## Store Types

Store type values:
- `LocalEncryptedDatabase`
- `AzureKeyVault`
- `KubernetesSecrets`
- `EnvironmentVariableName`
- `GenericExternalReference`

## Secret Store Response

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "workspaceId": "00000000-0000-0000-0000-000000000000",
  "name": "Elsa Control engine credentials",
  "provider": "Local encrypted database",
  "type": "LocalEncryptedDatabase",
  "description": "Engine API credentials for platform command dispatch",
  "status": "Active",
  "createdAt": "2026-06-08T12:00:00Z",
  "updatedAt": "2026-06-08T12:00:00Z",
  "archivedAt": null
}
```

Compatibility:
- Existing `provider` remains available for existing callers and display compatibility.
- New callers use `type` for behavior and validation.

## Create Or Update Secret Store

Request:

```json
{
  "name": "Elsa Control engine credentials",
  "type": "LocalEncryptedDatabase",
  "provider": null,
  "description": "Engine API credentials for platform command dispatch"
}
```

Behavior:
- `type` is required for new engine credential stores.
- `provider` remains accepted for compatibility and may be derived from `type` when omitted.
- Raw secret values and provider tokens are rejected.

## Credential Reference Response

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "workspaceId": "00000000-0000-0000-0000-000000000000",
  "secretStoreId": "00000000-0000-0000-0000-000000000000",
  "secretStoreName": "Elsa Control engine credentials",
  "secretStoreProvider": "Local encrypted database",
  "secretStoreType": "LocalEncryptedDatabase",
  "name": "Dev engine API",
  "reference": "local://engine/dev-api",
  "description": "Credential used by Elsa Control to call the Dev engine",
  "status": "Active",
  "verificationStatus": "Unverified",
  "lastVerifiedAt": null,
  "hasProtectedSecret": true,
  "usageCount": 1,
  "createdAt": "2026-06-08T12:00:00Z",
  "updatedAt": "2026-06-08T12:00:00Z",
  "archivedAt": null
}
```

Compatibility:
- Existing `reference` remains the safe locator/display value.
- `hasProtectedSecret` is true only when local protected credential material has been submitted.
- Responses never include raw credential material.

## Create Credential Reference

Local encrypted database request:

```json
{
  "name": "Dev engine API",
  "reference": "local://engine/dev-api",
  "description": "Credential used by Elsa Control to call the Dev engine",
  "secretValue": "submitted once over the request body"
}
```

External provider request:

```json
{
  "name": "Prod engine API",
  "reference": "kv://elsa-control/prod/engine-api",
  "description": "Credential used by Elsa Control to call the Prod engine"
}
```

Behavior:
- Local encrypted database references require a secret value on create unless explicitly created as a placeholder.
- External provider references reject raw `secretValue`.
- `reference` is a safe locator. It may be a Key Vault URI, Kubernetes namespace/name/key locator, environment variable name, or generic external reference.

## Rotate Local Credential Reference

`POST /credential-references/{credentialReferenceId}/rotate`

Request:

```json
{
  "secretValue": "new submitted credential value"
}
```

Behavior:
- Allowed only for local encrypted database references.
- Replaces protected credential material.
- Does not return the submitted value.

## Credential Usage

`GET /credential-references/{credentialReferenceId}/usage`

Response:

```json
{
  "items": [
    {
      "engineId": "00000000-0000-0000-0000-000000000000",
      "engineName": "dev-weu-01",
      "applicationId": "00000000-0000-0000-0000-000000000000",
      "applicationName": "Claims",
      "environmentId": "00000000-0000-0000-0000-000000000000",
      "environmentName": "Dev"
    }
  ]
}
```

## Engine Registration With Deferred Credentials

Request:

```json
{
  "name": "dev-weu-01",
  "baseUrl": "https://localhost:7294",
  "credentialReferenceId": null,
  "credentialAssignmentStatus": "Deferred",
  "capabilities": [],
  "controls": [],
  "hostingProvider": null
}
```

Behavior:
- A missing `credentialReferenceId` is accepted when credentials are deferred.
- Engine detail responses clearly show deferred credentials and command limitations.
- Updating an engine can assign or change `credentialReferenceId` later.
