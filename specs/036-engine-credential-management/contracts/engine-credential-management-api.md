# API Contract: Engine Credential Management

This feature reuses existing workspace deployment credential endpoints. No new endpoint is planned unless implementation discovers a missing behavior.

## Required existing endpoints

### List stores

`GET /api/workspaces/{workspaceId}/deployments/secret-stores`

Returns active and archived workspace engine credential store metadata.

### Create store

`POST /api/workspaces/{workspaceId}/deployments/secret-stores`

Request fields:
- `name`
- `provider`
- `type`
- `description`

### Update store

`PUT /api/workspaces/{workspaceId}/deployments/secret-stores/{secretStoreId}`

Updates safe store metadata supported by the existing contract.

### Archive store

`POST /api/workspaces/{workspaceId}/deployments/secret-stores/{secretStoreId}/archive`

Archives the store while keeping historical references understandable.

### List references

`GET /api/workspaces/{workspaceId}/deployments/credential-references`

Returns active and archived workspace credential reference metadata with usage counts and protected-secret presence flags.

### Create reference

`POST /api/workspaces/{workspaceId}/deployments/secret-stores/{secretStoreId}/credential-references`

Request fields:
- `name`
- `reference`
- `description`
- `secretValue` only for local encrypted database stores

### Update reference

`PUT /api/workspaces/{workspaceId}/deployments/credential-references/{credentialReferenceId}`

Updates safe metadata/locator fields supported by the existing contract.

### Rotate local reference

`POST /api/workspaces/{workspaceId}/deployments/credential-references/{credentialReferenceId}/rotate`

Request fields:
- `secretValue`

Only local encrypted database references may accept protected secret material.

### Get reference usage

`GET /api/workspaces/{workspaceId}/deployments/credential-references/{credentialReferenceId}/usage`

Returns safe engine usage records:
- Engine ID/name
- Application ID/name
- Environment ID/name

### Archive reference

`POST /api/workspaces/{workspaceId}/deployments/credential-references/{credentialReferenceId}/archive`

Archives the reference and prevents new assignment while preserving existing engine assignment readability.

## Contract invariants

- Responses must not include raw secret values or decrypted credential material.
- External store references must reject submitted raw secret values.
- Mutations require existing deployment setup permissions.
- All IDs are scoped to the selected workspace.
