# Quickstart: Deployment Artifact Packaging

## Goal

Verify that a deployment manifest can be built into a deterministic artifact, inspected, and checksum-verified without a deployment engine, CLI, API, or target environment.

## Sample Workspace

```text
samples/basic-workflow-deployment/
├── manifest.yaml
├── workflows/
│   └── order-approval.json
└── recipes/
    └── initialize-sales.yaml
```

Sample manifest:

```yaml
apiVersion: platform.elsa.io/v1alpha1
kind: EnvironmentManifest
metadata:
  name: sales-staging
  version: 2026.05.20.1
  environment: staging
resources:
  workflows:
    - id: order-approval
      path: workflows/order-approval.json
  recipes:
    - id: initialize-sales
      path: recipes/initialize-sales.yaml
```

## Expected Folder Artifact

```text
artifacts/sales-staging/
├── artifact.json
├── checksums.json
├── manifest/
│   └── manifest.yaml
└── payload/
    ├── workflows/
    │   └── order-approval.json
    └── recipes/
        └── initialize-sales.yaml
```

## Verification Flow

1. Parse `manifest.yaml` with `ManifestReader`.
2. Normalize the manifest with `ManifestNormalizer`.
3. Build a folder artifact from the manifest and workspace root.
4. Inspect the folder artifact and verify:
   - `Succeeded` is true.
   - Layout version is `platform.elsa.io/deployment-artifact/v1alpha1`.
   - Artifact ID starts with `sha256:`.
   - Manifest, metadata, payload, and checksum entries are present.
5. Build a ZIP artifact from the same workspace.
6. Inspect the ZIP artifact and verify it returns the same logical resource and checksum inventory as the folder artifact.
7. Modify one payload file and inspect again.
8. Verify inspection fails with `artifact.checksum.mismatch`.

## Commands

Focused verification after implementation:

```bash
dotnet test tests/Elsa.Platform.Deployment.Artifacts.Tests/Elsa.Platform.Deployment.Artifacts.Tests.csproj
```

Full verification before PR:

```bash
dotnet test
git diff --check
```
