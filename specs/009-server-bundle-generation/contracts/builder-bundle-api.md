# Contract: Builder Bundle API

## Trust Model

Bundle generation is protected from direct untrusted browser callers.

Initial trusted clients:

- Lovable/Supabase Edge Function or equivalent frontend proxy using dedicated builder-client credentials.
- Future CLI or automation clients using dedicated builder-client credentials.
- Workspace routes additionally require the existing trusted workspace identity context and workspace membership.

Rules:

- Anonymous Runtime Builder users may generate bundles only through a trusted frontend/proxy client.
- The platform does not accept arbitrary browser-provided trusted-client headers.
- Builder-client credentials are least-privilege credentials for builder bundle generation and must not grant broad admin API access.
- Generated ad hoc files are returned in the response only and are not retrievable later.
- Secret values and private feed credentials are never returned, logged, or persisted.

## POST /api/builder/bundle

Generates an ephemeral bundle for packages and sources visible through the public builder catalog.

Authentication:

- Requires dedicated trusted frontend/proxy or API builder-client credentials.
- Does not require an authenticated end-user account or saved configuration.

Request:

```json
{
  "image": {
    "slug": "elsa-pro-combined",
    "tag": "latest",
    "hostPort": 8080,
    "envOverrides": {
      "ASPNETCORE_ENVIRONMENT": "Development"
    }
  },
  "packages": [
    {
      "sourceId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "packageId": "Elsa.Persistence.PostgreSql",
      "version": "1.0.2",
      "selectedFeatures": ["postgresql-persistence"],
      "settings": {
        "postgresql-persistence": {
          "ConnectionString": ""
        }
      }
    }
  ],
  "packageSources": [
    {
      "sourceId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    }
  ],
  "infrastructure": [
    {
      "kind": "database",
      "providerId": "postgres-compose",
      "strategy": "compose-sidecar",
      "settings": {}
    }
  ],
  "localPackages": {
    "enabled": false,
    "directoryPath": "packages"
  }
}
```

Successful response:

```json
{
  "bundleId": "preview",
  "files": [
    {
      "path": "config.json",
      "language": "json",
      "contentType": "application/json",
      "required": true,
      "contents": "{\n  \"elsa\": {}\n}\n"
    },
    {
      "path": "Program.Generated.cs",
      "language": "csharp",
      "contentType": "text/x-csharp",
      "required": false,
      "contents": "// Optional reference output\n"
    }
  ],
  "findings": []
}
```

Warning response:

```json
{
  "bundleId": "preview",
  "files": [
    {
      "path": "README.md",
      "language": "markdown",
      "contentType": "text/markdown",
      "required": true,
      "contents": "# Elsa Runtime\n"
    }
  ],
  "findings": [
    {
      "level": "warning",
      "code": "setting.placeholder",
      "message": "ConnectionString will be emitted as a placeholder.",
      "scope": "feature:postgresql-persistence/setting:ConnectionString"
    }
  ]
}
```

Blocked response:

```json
{
  "bundleId": "preview",
  "files": [],
  "findings": [
    {
      "level": "error",
      "code": "package.missing",
      "message": "Elsa.Persistence.PostgreSql 1.0.2 is not indexed.",
      "scope": "package:Elsa.Persistence.PostgreSql"
    }
  ]
}
```

Required first-release files:

- `config.json`
- `packages.lock.json`
- `docker-compose.yml`
- `.env.example`
- `README.md`

Optional first-release files:

- `Program.Generated.cs`

Errors:

- `400 Bad Request`: request JSON is malformed or missing the top-level required shape.
- `401 Unauthorized`: dedicated builder-client credentials are missing or invalid.
- `200 OK` with error findings and no files: request is syntactically valid but generation is blocked by domain validation.

## POST /api/workspaces/{workspaceId}/builder/bundle

Generates an ephemeral bundle for packages and sources visible to the selected workspace.

Authentication:

- Requires trusted workspace identity context.
- Caller must be a member of `{workspaceId}`.
- Direct browser-supplied workspace identity is rejected by existing workspace identity rules.

Behavior:

- Public browseable sources and workspace-owned sources visible to the workspace may be used.
- Private sources from other workspaces are not discoverable and cannot be used even if source IDs are known.
- Response shape matches `POST /api/builder/bundle`.

Errors:

- `401 Unauthorized`: no trusted identity context.
- `403 Forbidden`: authenticated identity is not a member of the workspace.
- `200 OK` with error findings and no files: workspace-visible validation blocks generation.

## Finding Levels

- `error`: blocks generation and returns no files.
- `warning`: files may be returned but users should review the message.
- `info`: advisory recommendation.

## File Safety Rules

- File paths are relative to the bundle root.
- File paths must not contain parent-directory traversal.
- Secret setting values are represented as placeholders, not raw values.
- Source URLs in returned files must be sanitized unless the user explicitly supplied a non-secret local placeholder.
- Generated contents are not stored for later retrieval in this feature.
