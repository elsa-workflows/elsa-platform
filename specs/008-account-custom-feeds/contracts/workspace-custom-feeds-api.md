# Contract: Workspace Custom Feeds API

## Identity Context

Workspace APIs require a trusted identity context. The first implementation supports a narrow development/server-to-server adapter:

```http
X-Catalog-Identity-Issuer: https://elsaworkflows.io
X-Catalog-Identity-Subject: lovable-user-id-or-customer-subject
X-Catalog-Identity-Email: optional@example.test
X-Catalog-Identity-Name: Optional Display Name
```

Rules:

- `Issuer` and `Subject` are required.
- The adapter is accepted only when enabled by configuration.
- Enabled trusted-header identity is accepted only from configured proxy IPs/CIDR ranges (`Authentication:WorkspaceTrustedHeaders:AllowedProxyNetworks`); direct clients must not be able to provide these headers.
- Browser-supplied user IDs, account IDs, or workspace IDs without this trusted context are rejected.
- Later OIDC/JWT validation must map to the same trusted identity model.

## GET /api/me/workspaces

Returns catalog account and workspace context for the authenticated identity. Creates the account and personal workspace on first use.

Response:

```json
{
  "account": {
    "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "displayName": "Ada Lovelace",
    "email": "ada@example.test"
  },
  "workspaces": [
    {
      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "name": "Ada Lovelace",
      "kind": "Personal",
      "role": "Owner"
    }
  ]
}
```

Errors:

- `401 Unauthorized`: no trusted identity context.

## GET /api/workspaces/{workspaceId}/sources

Lists public browseable sources plus private sources owned by the selected workspace.

Response:

```json
[
  {
    "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
    "name": "Elsa Official",
    "url": "https://api.nuget.org/v3/index.json",
    "ownership": "Public",
    "packageCount": 42
  },
  {
    "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
    "name": "Company Feed",
    "url": "https://nuget.example.test/v3/index.json",
    "ownership": "Workspace",
    "packageCount": 7
  }
]
```

Errors:

- `401 Unauthorized`: no trusted identity context.
- `403 Forbidden`: authenticated identity is not a member of the workspace.

## POST /api/workspaces/{workspaceId}/sources

Creates a private workspace-owned NuGet source.

Request:

```json
{
  "name": "Company Feed",
  "url": "https://nuget.example.test/v3/index.json",
  "enabled": true,
  "includePatterns": ["Elsa.*"],
  "excludePatterns": [],
  "versionDiscoveryPolicy": "AllVersions"
}
```

Response:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "name": "Company Feed",
  "url": "https://nuget.example.test/v3/index.json",
  "ownership": "Workspace",
  "packageCount": 0
}
```

Errors:

- `400 Bad Request`: invalid source name, URL, unsupported credentials, unsupported source type, or duplicate source URL within the workspace.
- `401 Unauthorized`: no trusted identity context.
- `403 Forbidden`: caller is not a workspace source administrator or entitlement does not allow creation.

## PUT /api/admin/workspaces/{workspaceId}/entitlements

Operator endpoint to replace the latest entitlement snapshot for a workspace.

Authentication:

- Existing admin API key or admin dashboard cookie policy.

Request:

```json
{
  "canCreateCustomSources": true,
  "maxSources": 5,
  "maxPackagesIndexed": 500,
  "maxVersionsPerPackage": 20,
  "maxSyncsPerDay": 25,
  "privateFeedsEnabled": false
}
```

Response:

```json
{
  "workspaceId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "canCreateCustomSources": true,
  "maxSources": 5,
  "maxPackagesIndexed": 500,
  "maxVersionsPerPackage": 20,
  "maxSyncsPerDay": 25,
  "privateFeedsEnabled": false,
  "syncedAt": "2026-05-18T00:00:00Z"
}
```

## Workspace Package Browsing

Workspace package browsing uses the existing source-qualified package identity and adds workspace membership to the visibility decision:

- `GET /api/workspaces/{workspaceId}/packages?sourceIds={sourceId}`
- `GET /api/workspaces/{workspaceId}/sources/{sourceId}/packages/{packageId}`
- `GET /api/workspaces/{workspaceId}/builder/catalog?sourceIds={sourceId}`
- `POST /api/workspaces/{workspaceId}/builder/resolve`
- `POST /api/workspaces/{workspaceId}/compatibility/check`

Rules:

- Public catalog-owned browseable sources remain visible.
- Workspace-owned sources are visible only to members of the owning workspace.
- Non-member and anonymous callers receive `401`, `403`, empty lists, or `404` as appropriate and never receive package-state metadata from private sources.
- Source URLs are sanitized in responses.
