# Quickstart: Server-Side Bundle Generation

## Local Setup

1. Run the API using the normal local development configuration.
2. Configure `Authentication:BuilderClientApiKey` for the trusted Lovable/Supabase proxy or local test client.
3. Seed at least one public browseable source with valid, approved, listed package versions and feature manifests.
4. Ensure runtime image metadata is available for the requested image slug.

## Scenario 1: Trusted Client Generates A Minimal Bundle

```http
POST /api/builder/bundle
X-API-Key: builder-dev-key
Content-Type: application/json

{
  "image": {
    "slug": "elsa-pro-combined",
    "tag": "latest",
    "hostPort": 8080,
    "envOverrides": {}
  },
  "packages": [],
  "packageSources": [],
  "infrastructure": [],
  "localPackages": {
    "enabled": false,
    "directoryPath": "packages"
  }
}
```

Expected:

- `200 OK`
- Response includes required files: `config.json`, `packages.lock.json`, `docker-compose.yml`, `.env.example`, and `README.md`.
- `Program.Generated.cs` may appear with `required: false`.
- Findings are empty or warning-only.

Use the builder-client key, not the admin API key. In local test configuration this is `builder-dev-key`; production deployments should set a distinct least-privilege value.

## Scenario 2: Direct Browser Caller Is Rejected

Repeat Scenario 1 without trusted client credentials.

Expected:

- `401 Unauthorized`
- No bundle files are returned.

## Scenario 3: Blocking Domain Errors Return Findings Only

Submit a syntactically valid request with an unknown package version or unknown runtime image slug.

Expected:

- `200 OK`
- `files` is an empty array.
- `findings` contains at least one `error`.
- No misleading partial deployment bundle is returned.

## Scenario 4: Warning-Only Generation Returns Files

Submit a request that can render but requires placeholders for non-secret missing values.

Expected:

- `200 OK`
- Required files are returned.
- `findings` contains one or more `warning` entries.
- Secret values are not present in file contents or findings.

## Scenario 5: Workspace Bundle Uses Workspace Visibility

```http
POST /api/workspaces/{workspaceId}/builder/bundle
X-Catalog-Identity-Issuer: https://elsaworkflows.io
X-Catalog-Identity-Subject: user-123
Content-Type: application/json

{
  "image": {
    "slug": "elsa-pro-combined",
    "tag": "latest",
    "envOverrides": {}
  },
  "packages": [
    {
      "sourceId": "workspace-source-id",
      "packageId": "Elsa.Private",
      "version": "1.0.0",
      "selectedFeatures": [],
      "settings": {}
    }
  ],
  "packageSources": [
    {
      "sourceId": "workspace-source-id"
    }
  ],
  "infrastructure": [],
  "localPackages": {
    "enabled": false,
    "directoryPath": "packages"
  }
}
```

Expected:

- Workspace member receives bundle files or domain findings based on visible packages.
- Non-member receives `403 Forbidden`.
- Anonymous caller receives `401 Unauthorized`.
- Source IDs from other workspaces do not leak private package metadata.

## Scenario 6: Generated Files Are Ephemeral

After any successful ad hoc generation, attempt to retrieve the same `bundleId` from platform storage.

Expected:

- There is no retrieval endpoint for ad hoc bundle files in this feature.
- Operational logs may contain non-secret generation diagnostics only.

## Migration Difference Notes

The backend output is the platform contract. Browser parity fixtures should flag rollout-relevant differences, but these categories are accepted:

- deterministic formatting differences in JSON, YAML, Markdown, or C# reference output,
- safer placeholder output for secret or missing values,
- sanitized package source URLs,
- simplified Docker Compose snippets where the backend contract remains deployable,
- optional omission or inclusion of `Program.Generated.cs` as reference-only output.

## Validation Commands

```bash
dotnet build ValenceControl.sln --no-restore
dotnet test ValenceControl.sln --no-build
```
