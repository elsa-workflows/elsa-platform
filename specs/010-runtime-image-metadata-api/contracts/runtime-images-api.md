# Contract: Runtime Image Metadata API

## GET /api/builder/catalog

The builder catalog response gains an `images` array.

Response excerpt:

```json
{
  "images": [
    {
      "slug": "elsa-pro-combined",
      "displayName": "Elsa Professional Combined",
      "description": "Combined Elsa Server and Studio runtime.",
      "image": "elsaworkflows/elsa-pro-combined",
      "availableTags": ["latest"],
      "defaultTag": "latest",
      "defaultPort": 8080,
      "hostPort": 8080,
      "containerName": "elsa-pro-combined",
      "licenseTier": "Professional",
      "stability": "Stable",
      "capabilities": ["server", "studio"],
      "envVars": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "displayName": "Environment",
          "required": false,
          "secret": false,
          "defaultValue": "Production",
          "group": "Runtime",
          "advanced": false
        }
      ],
      "deploymentHints": {
        "supportsDockerCompose": true,
        "supportsKubernetes": true,
        "requiresCompanionServer": false,
        "needsSharedNetwork": false
      },
      "docs": {
        "dockerHubUrl": "https://hub.docker.com/",
        "containerPaths": []
      }
    }
  ],
  "packages": [],
  "infrastructureProviders": []
}
```

Rules:

- `images` must include `elsa-pro-server`, `elsa-pro-studio`, and `elsa-pro-combined`.
- Deployment-affecting fields are authoritative from the backend.
- Purely visual frontend fallback fields may remain local during migration.

## Bundle And Planner Usage

Bundle and planner requests that include image slug/tag must resolve them through the runtime image catalog.

Unknown slug response behavior:

```json
{
  "files": [],
  "findings": [
    {
      "level": "error",
      "code": "runtimeImage.unknown",
      "message": "Runtime image elsa-pro-unknown is not supported.",
      "scope": "image:elsa-pro-unknown"
    }
  ]
}
```
