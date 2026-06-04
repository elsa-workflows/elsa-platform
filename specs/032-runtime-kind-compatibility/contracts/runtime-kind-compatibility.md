# Contract: Runtime Kind Compatibility

## Manifest Contract

Package-level compatibility supports runtime kinds:

```json
{
  "compatibility": {
    "runtimeKinds": ["elsa.server"]
  }
}
```

Feature-level compatibility supports runtime kinds and overrides the package-level default:

```json
{
  "features": [
    {
      "id": "server.postgresql",
      "compatibility": {
        "runtimeKinds": ["elsa.server"]
      }
    },
    {
      "id": "studio.dashboard-widget",
      "compatibility": {
        "runtimeKinds": ["elsa.studio"]
      }
    }
  ]
}
```

Rules:

- Runtime kind values are strings, not manifest enum values.
- Official values are `elsa.server` and `elsa.studio`.
- Custom values are allowed when valid.
- Empty lists, blank values, whitespace values, and duplicate values are invalid.
- Omitted package and feature runtime kinds default to effective `elsa.server` compatibility for existing manifests.

## Catalog Projection Contract

Catalog package and feature projections expose effective runtime compatibility to consumers.

Package projection:

```json
{
  "packageId": "Elsa.Persistence.PostgreSql",
  "version": "4.0.0",
  "compatibility": {
    "runtimeKinds": ["elsa.server"],
    "runtimeCapabilities": []
  }
}
```

Feature projection:

```json
{
  "featureId": "postgresql",
  "compatibility": {
    "runtimeKinds": ["elsa.server"]
  }
}
```

Consumer rules:

- Filter packages and features by exact case-insensitive runtime-kind match.
- Treat runtime kind as separate from runtime capabilities.
- Preserve unknown valid values in projections.
- Explain incompatible packages or features using the target runtime and effective runtime kinds.

## Runtime Builder Contract

Runtime Builder targets Elsa Server and uses `elsa.server` compatibility when selecting packages and features.

Rules:

- Existing undeclared packages remain selectable in Elsa Server builder flows.
- Studio-only packages and features are excluded from Elsa Server builder flows.
- Mixed packages can appear in Elsa Server builder flows, but only server-compatible features are selectable.
