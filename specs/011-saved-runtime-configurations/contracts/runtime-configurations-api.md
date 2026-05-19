# Contract: Runtime Configurations API

Workspace routes require the existing trusted workspace identity context.

## GET /api/workspaces/{workspaceId}/runtime-configurations

Lists active configurations for a workspace.

## POST /api/workspaces/{workspaceId}/runtime-configurations

Creates a configuration.

```json
{
  "name": "Production Elsa Runtime",
  "description": "PostgreSQL + RabbitMQ",
  "intent": {}
}
```

## GET /api/workspaces/{workspaceId}/runtime-configurations/{id}

Returns saved intent.

## PUT /api/workspaces/{workspaceId}/runtime-configurations/{id}

Updates mutable draft fields and intent.

## DELETE /api/workspaces/{workspaceId}/runtime-configurations/{id}

Soft-deletes the configuration.

## POST /api/workspaces/{workspaceId}/runtime-configurations/{id}/clone

Creates a separate editable copy.

## POST /api/workspaces/{workspaceId}/runtime-configurations/{id}/versions

Creates an immutable snapshot.

## GET /api/workspaces/{workspaceId}/runtime-configurations/{id}/versions

Lists snapshots.

## POST /api/workspaces/{workspaceId}/runtime-configurations/{id}/bundle

Generates an ephemeral bundle using the saved draft intent.
