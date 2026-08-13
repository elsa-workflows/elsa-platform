# Contracts: Deployment Setup Domain Flow

All routes are under `/api/workspaces/{workspaceId}/deployments` and use existing workspace access resolution plus deployment permissions.

## Environment Setup

### Create environment

`POST /applications/{applicationId}/environments`

Request:

```json
{
  "name": "Test",
  "tierId": "00000000-0000-0000-0000-000000000000"
}
```

Behavior:
- Creates only the environment.
- Does not create an engine registration.
- Requires deployment setup permission.

## Secret Stores

### List secret stores

`GET /secret-stores`

Response:

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "workspaceId": "00000000-0000-0000-0000-000000000000",
      "name": "Valence Control Key Vault",
      "provider": "Azure Key Vault",
      "description": "Shared deployment credential vault",
      "status": "Active",
      "createdAt": "2026-06-07T12:00:00Z",
      "updatedAt": "2026-06-07T12:00:00Z"
    }
  ]
}
```

### Create secret store

`POST /secret-stores`

Request:

```json
{
  "name": "Valence Control Key Vault",
  "provider": "Azure Key Vault",
  "description": "Shared deployment credential vault"
}
```

Behavior:
- Stores metadata only.
- Requires deployment setup permission.
- Rejects duplicate active names in the workspace.

### Update secret store

`PUT /secret-stores/{secretStoreId}`

Request:

```json
{
  "name": "Valence Control Key Vault",
  "provider": "Azure Key Vault",
  "description": "Shared deployment credential vault"
}
```

### Archive secret store

`POST /secret-stores/{secretStoreId}/archive`

Behavior:
- Marks the store archived.
- Hides it from new engine registration options.
- Existing engines remain readable.

## Credential References

### List credential references

`GET /secret-stores/{secretStoreId}/credential-references`

Response:

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "workspaceId": "00000000-0000-0000-0000-000000000000",
      "secretStoreId": "00000000-0000-0000-0000-000000000000",
      "name": "Test engine API",
      "reference": "kv://acme/test/engine-api",
      "description": "Engine API credential for Test",
      "status": "Active",
      "verificationStatus": "Unverified",
      "lastVerifiedAt": null,
      "createdAt": "2026-06-07T12:00:00Z",
      "updatedAt": "2026-06-07T12:00:00Z"
    }
  ]
}
```

### Create credential reference

`POST /secret-stores/{secretStoreId}/credential-references`

Request:

```json
{
  "name": "Test engine API",
  "reference": "kv://acme/test/engine-api",
  "description": "Engine API credential for Test"
}
```

Behavior:
- Stores reference metadata only.
- Requires deployment setup permission.
- Rejects duplicate active names in the selected store.

### Update credential reference

`PUT /credential-references/{credentialReferenceId}`

Request:

```json
{
  "name": "Test engine API",
  "reference": "kv://acme/test/engine-api",
  "description": "Engine API credential for Test"
}
```

### Archive credential reference

`POST /credential-references/{credentialReferenceId}/archive`

Behavior:
- Marks the reference archived.
- Hides it from new engine registration options.
- Existing engines remain readable.

## Engine Registration

### Register engine

`POST /environments/{environmentId}/engines`

Request:

```json
{
  "name": "test-weu-01",
  "baseUrl": "https://test-engine.example.com",
  "credentialReferenceId": "00000000-0000-0000-0000-000000000000",
  "capabilities": [],
  "controls": [],
  "hostingProvider": null
}
```

Compatibility:
- Existing `credentialProvider` and `credentialReference` string request fields remain accepted for legacy callers.
- New console flows use `credentialReferenceId` so provider/reference strings are derived from registered metadata.
