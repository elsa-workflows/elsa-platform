# Quickstart: Deployment Template Expansion

## Scenario 1: Default Target

Omit `target`.

Expected: Docker Compose files are returned.

## Scenario 2: Azure Container Apps

Set `target` to `azure-container-apps`.

Expected: Azure template files and README are returned.

## Scenario 3: Kubernetes/Helm

Set `target` to `kubernetes-helm`.

Expected: Helm/Kubernetes files and README are returned.

## Validation Commands

```bash
dotnet build ElsaControl.sln --no-restore
dotnet test ElsaControl.sln --no-build
```

## Supported Targets

Pass `target` to bundle generation:

- `docker-compose` returns the default Compose bundle.
- `azure-container-apps` adds `azure-container-app.bicep`.
- `kubernetes-helm` adds `helm/Chart.yaml`, `helm/values.yaml`, and `helm/templates/deployment.yaml`.

Unsupported targets return `deploymentTarget.unsupported` and no files.
