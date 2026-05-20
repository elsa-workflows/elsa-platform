# Contract: Builder Plan API

## POST /api/builder/plan

Request:

```json
{
  "intent": {
    "image": { "slug": "elsa-pro-combined", "tag": "latest" },
    "selectedCapabilities": ["postgresql-persistence"],
    "packages": [],
    "features": [],
    "settings": {},
    "infrastructure": []
  }
}
```

Response:

```json
{
  "resolved": {
    "image": { "slug": "elsa-pro-combined", "tag": "latest" },
    "packages": [],
    "features": [],
    "infrastructure": []
  },
  "autoAdded": {
    "packages": [],
    "features": [],
    "infrastructure": []
  },
  "findings": []
}
```

Workspace variant: `POST /api/workspaces/{workspaceId}/builder/plan`.
