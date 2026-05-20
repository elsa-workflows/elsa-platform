# Contract: Deployment Artifact Layout

## Layout Version

Phase 1 artifacts use:

```text
platform.elsa.io/deployment-artifact/v1alpha1
```

Readers must reject unsupported layout versions with `artifact.layout.unsupported`.

## Logical Layout

Folder and ZIP artifacts expose the same logical paths:

```text
artifact.json
manifest/
  manifest.yaml | manifest.json
checksums.json
payload/
  workflows/<file>
  recipes/<file>
  resources/<extension-files>
```

Rules:

- All paths are artifact-relative and use `/`.
- `artifact.json`, `checksums.json`, and one manifest snapshot are required.
- Payload paths are copied only from manifest-declared relative paths.
- Readers must reject absolute paths, empty segments, `.`, `..`, backslash traversal, and archive entries that escape the artifact root.

## artifact.json

Required fields:

```json
{
  "layoutVersion": "platform.elsa.io/deployment-artifact/v1alpha1",
  "artifactId": "sha256:<content-digest>",
  "createdAt": "2026-05-20T00:00:00Z",
  "manifest": {
    "name": "sales-staging",
    "version": "2026.05.20.1",
    "environment": "staging",
    "labels": {},
    "annotations": {}
  },
  "resources": [
    {
      "type": "workflow-definition",
      "logicalId": "order-approval",
      "scope": null,
      "version": "1.0.0",
      "desiredStateHash": "sha256:..."
    }
  ],
  "contentDigest": {
    "algorithm": "sha256",
    "value": "..."
  }
}
```

Identity rules:

- `createdAt` is informational and excluded from `artifactId`.
- `artifactId` is derived from canonical artifact content, including manifest snapshot, payload bytes, and checksum inventory.
- Environment and target names are not part of identity except when already present in manifest metadata.

## checksums.json

Required fields:

```json
{
  "algorithm": "sha256",
  "entries": [
    {
      "path": "manifest/manifest.yaml",
      "kind": "Manifest",
      "size": 1234,
      "digest": "..."
    },
    {
      "path": "payload/workflows/order-approval.json",
      "kind": "Payload",
      "size": 5678,
      "digest": "..."
    }
  ]
}
```

Rules:

- Phase 1 supports only `sha256`.
- Readers must report missing checksum entries with `artifact.checksum.missing`.
- Readers must report changed bytes with `artifact.checksum.mismatch`.
- Readers must report payload files not listed in the checksum inventory with `artifact.payload.unexpected`.

## Public Contract Shape

The package exposes library contracts, not CLI or HTTP contracts:

```text
IDeploymentArtifactBuilder
  BuildFolderAsync(options, cancellationToken)
  BuildZipAsync(options, cancellationToken)

IDeploymentArtifactReader
  InspectFolderAsync(path, cancellationToken)
  InspectZipAsync(path, cancellationToken)
```

All methods return result objects with diagnostics instead of throwing for expected invalid input. Unexpected programming errors may still throw.
