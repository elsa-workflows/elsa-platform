# Quickstart: Account-Owned Custom Feeds

## Local Setup

1. Run the API using the normal local development configuration.
2. Enable the trusted workspace identity adapter for local/test use.
3. Use a trusted identity header set for workspace APIs:

```http
X-Catalog-Identity-Issuer: https://elsaworkflows.io
X-Catalog-Identity-Subject: user-123
X-Catalog-Identity-Email: ada@example.test
X-Catalog-Identity-Name: Ada Lovelace
```

## Scenario 1: First Sign-In Provisions Account And Workspace

```http
GET /api/me/workspaces
```

Expected:

- `200 OK`
- Response includes one account and one personal workspace.
- Calling the endpoint again with the same issuer and subject returns the same account and workspace IDs.

## Scenario 2: Workspace Cannot Create Sources Without Entitlement

```http
POST /api/workspaces/{workspaceId}/sources
Content-Type: application/json

{
  "name": "Company Feed",
  "url": "https://nuget.example.test/v3/index.json",
  "enabled": true,
  "includePatterns": ["Elsa.*"],
  "excludePatterns": [],
  "versionDiscoveryPolicy": "AllVersions"
}
```

Expected:

- `403 Forbidden`
- No source is created.

## Scenario 3: Operator Grants Custom Source Entitlement

```http
PUT /api/admin/workspaces/{workspaceId}/entitlements
X-API-Key: local-dev-key
Content-Type: application/json

{
  "canCreateCustomSources": true,
  "maxSources": 1,
  "maxPackagesIndexed": 500,
  "maxVersionsPerPackage": 20,
  "maxSyncsPerDay": 25,
  "privateFeedsEnabled": false
}
```

Expected:

- `200 OK`
- Response reflects the latest entitlement snapshot.

## Scenario 4: Entitled Workspace Creates Custom Source

Repeat the source creation request from Scenario 2.

Expected:

- `200 OK` or `201 Created`
- Response includes `ownership: "Workspace"`.
- URL has no credentials, query string, or fragment.
- A second source creation fails when `maxSources` is `1`.

## Scenario 5: Workspace Source Visibility

```http
GET /api/workspaces/{workspaceId}/sources
```

Expected:

- Authenticated workspace member sees public browseable sources and their workspace source.
- Anonymous `GET /api/sources` does not include the workspace source.

## Validation Commands

```bash
dotnet build Elsa.PackageCatalog.sln --no-restore
dotnet test Elsa.PackageCatalog.sln --no-build
```
