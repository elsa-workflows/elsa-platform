# Contract: Workspace Weaver API

All endpoints are workspace-scoped and must enforce the same workspace membership and permission model as existing workspace APIs.

Base path:

```text
/api/workspaces/{workspaceId}/weaver
```

## GET /configuration

Returns safe effective Weaver availability and model configuration for the current user/workspace.

Response:

```json
{
  "enabled": true,
  "providerMode": "BringYourOwnKey",
  "model": "gpt-5",
  "reasoningEffort": "medium",
  "streamingEnabled": true,
  "modes": ["Inspect", "Plan"],
  "disabledReason": null
}
```

## POST /sessions

Creates a backend Weaver session.

Request:

```json
{
  "routePath": "/admin/deployments/applications/{applicationId}/environments/{environmentId}",
  "mode": "Inspect",
  "context": {
    "applicationId": "00000000-0000-0000-0000-000000000000",
    "environmentId": "00000000-0000-0000-0000-000000000000"
  }
}
```

Response:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "status": "Active",
  "mode": "Inspect",
  "createdAt": "2026-06-07T00:00:00Z"
}
```

## GET /sessions/{sessionId}

Returns safe session state, messages, visible tool-call summaries, and plans.

## POST /sessions/{sessionId}/messages

Sends or queues a prompt to Weaver.

Request:

```json
{
  "prompt": "What is wrong on this page?",
  "mode": "Inspect",
  "delivery": "Immediate"
}
```

Response:

```json
{
  "messageId": "00000000-0000-0000-0000-000000000000",
  "sessionStatus": "Active"
}
```

## GET /sessions/{sessionId}/events

Streams Server-Sent Events for assistant deltas, tool activity, plan updates, errors, waiting states, and idle/completed states.

Event examples:

```json
{ "type": "assistant.delta", "content": "Production is blocked because..." }
{ "type": "tool.started", "toolName": "get_environment_detail" }
{ "type": "tool.completed", "toolName": "get_environment_detail", "summary": "Environment Dev has 1 engine." }
{ "type": "plan.created", "planId": "00000000-0000-0000-0000-000000000000" }
{ "type": "session.idle" }
```

## POST /sessions/{sessionId}/cancel

Cancels the active agent turn if supported by the current runtime.

## POST /plans/{planId}/approvals

Approves or rejects a plan version.

Request:

```json
{
  "version": 1,
  "decision": "Approved",
  "confirmationId": "00000000-0000-0000-0000-000000000000",
  "reason": null
}
```

Response:

```json
{
  "planId": "00000000-0000-0000-0000-000000000000",
  "version": 1,
  "status": "Approved"
}
```

## POST /plans/{planId}/execute

Executes an approved plan version through existing platform services.

Request:

```json
{
  "version": 1
}
```

Response:

```json
{
  "executionId": "00000000-0000-0000-0000-000000000000",
  "status": "Queued",
  "linkedResources": []
}
```

## Security Rules

- Read endpoints require workspace read access plus domain-specific permissions when reading deployment/runtime/artifact data.
- Approval and execution endpoints require the permission required by the underlying mutation.
- All plan and session IDs must be verified to belong to the requested workspace.
- Raw provider credentials, secrets, tokens, and raw artifact payloads must not appear in any response.
