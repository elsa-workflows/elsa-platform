# Contract: Public Source-Scoped Catalog API

## Public Browse Sources

### `GET /api/sources`

Returns catalog-owned sources that anonymous and free users may browse.

Response:

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000001",
    "name": "Elsa Official",
    "url": "https://api.nuget.org/v3/index.json",
    "packageCount": 42
  }
]
```

Rules:

- Excludes disabled, deleted, non-browseable, private, and workspace-owned sources.
- Removes credentials, query strings, and fragments from URLs.

## Package List

### `GET /api/packages?sourceIds={sourceId}&sourceIds={sourceId}`

Returns public packages from selected browseable sources.

Rules:

- If source IDs are supplied, every returned package must belong to one of those sources.
- Unknown or inaccessible source IDs do not reveal private source metadata.
- Package identity in each response is source-qualified.

Response package shape:

```json
{
  "source": {
    "id": "00000000-0000-0000-0000-000000000001",
    "name": "Elsa Official",
    "url": "https://api.nuget.org/v3/index.json"
  },
  "packageId": "Elsa.Email",
  "latestVersion": "1.0.0",
  "versions": []
}
```

## Package Details

### `GET /api/sources/{sourceId}/packages/{packageId}`

Returns details for the package in the requested source only.

### `GET /api/sources/{sourceId}/packages/{packageId}/versions`

Returns visible versions for the package in the requested source only.

### `GET /api/sources/{sourceId}/packages/{packageId}/versions/{version}`

Returns one visible source-qualified package version.

Rules:

- Global package detail routes are not part of the new contract.
- Missing source/package/version combinations return not found.

## Builder Catalog

### `GET /api/builder/catalog?sourceIds={sourceId}&sourceIds={sourceId}`

Returns builder-ready packages filtered to selected sources plus infrastructure providers.

Rules:

- Package and feature records include source provenance.
- Unselected sources do not contribute packages or features.

## Builder Resolve

### `POST /api/builder/resolve`

Request:

```json
{
  "elsaVersion": "3.0.0",
  "dockerImageVersion": "3.0.0",
  "packages": [
    {
      "sourceId": "00000000-0000-0000-0000-000000000001",
      "packageId": "Elsa.Email",
      "version": "1.0.0",
      "selectedFeatures": ["email"]
    }
  ]
}
```

Rules:

- `sourceId`, `packageId`, and `version` are required for each selected package.
- Compatibility uses the source-qualified package version.
