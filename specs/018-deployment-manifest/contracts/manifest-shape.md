# Contract: v1alpha Environment Manifest

## YAML Shape

```yaml
apiVersion: platform.elsa.io/v1alpha1
kind: EnvironmentManifest
metadata:
  name: sales-staging
  version: 2026.05.20.1
  environment: staging
  labels:
    team: sales
  annotations:
    sourceCommit: abc123
resources:
  workflows:
    - id: order-approval
      path: workflows/order-approval.json
      activation: active
  variables:
    - key: orderTimeout
      value: 30
      scope: sales
  features:
    - id: sales
      state: enabled
  packages:
    - id: Acme.Sales
      version: 1.4.2
  recipes:
    - id: initialize-sales
      path: recipes/initialize-sales.yaml
```

## Resource Type Mapping

| Section | Resource type | Identity field | Phase 1 behavior |
| --- | --- | --- | --- |
| `workflows` | `workflowDefinition` | `id` | normalized deployable resource |
| `variables` | `variable` | `key` plus optional `scope` | normalized deployable resource |
| `features` | `feature` | `id` | descriptor resource |
| `packages` | `package` | `id` | descriptor resource |
| `recipes` | `recipe` | `id` | descriptor resource |

## Diagnostics

The manifest package emits `DeploymentDiagnostic` values with stable codes:

- `manifest.parse`
- `manifest.apiVersion.required`
- `manifest.apiVersion.unsupported`
- `manifest.kind.required`
- `manifest.kind.unsupported`
- `manifest.metadata.name.required`
- `manifest.resource.identity.required`
- `manifest.resource.duplicate`
- `manifest.resource.path.invalid`
- `manifest.resource.unsupported`

## Deferred Shape

- Environment overlays.
- Secret references.
- Artifact checksum manifests.
- Policy blocks.
- Promotion flows.
- Kubernetes CRD mapping.
