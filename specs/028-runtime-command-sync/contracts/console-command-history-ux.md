# Contract: Console Command History UX

## Deployment History Projection

Deployment history remains centered on deployment runs. Command lifecycle details appear as supporting events:

- Command created.
- Command claimed by runtime worker.
- Runtime heartbeat received.
- Runtime progress milestone.
- Runtime completed command.
- Runtime failed or rejected command.
- Command marked stale or recovery-required.

## Run Detail

Run detail can show:

- Command ID.
- Command action.
- Command status.
- Target runtime engine.
- Artifact or revision reference.
- Current lease state without exposing lease token.
- Attempt number.
- Last heartbeat.
- Safe diagnostics.
- Runtime result reference and observed digest.

## Safety Rules

- Lease tokens are never displayed.
- Raw artifact payloads, workflow definitions, credentials, tokens, connection strings, and secret values are never displayed.
- Webhook notifications are labeled as triggers, not as deployment authority.

## User States

- Pending command: runtime has not claimed work.
- Claimed/running command: runtime is processing work.
- Completed command: runtime reported success.
- Failed/rejected command: runtime reported a safe failure or compatibility rejection.
- Recovery required: command was stale or ambiguous and needs explicit operator/user recovery.
