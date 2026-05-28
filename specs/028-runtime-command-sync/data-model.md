# Data Model: Runtime Command Sync

## DeploymentCommand

Durable command linked to a deployment run and targeted at one registered runtime engine.

Fields:

- `id`: Command record ID.
- `workspaceId`: Tenant boundary.
- `runId`: Linked deployment run.
- `environmentId`: Target environment.
- `engineId`: Target runtime engine.
- `action`: Deploy, rollback, validate/dry-run, or future runtime action.
- `status`: Pending, claimed, running, completed, failed, rejected, cancelled, recovery-required, or expired.
- `artifactReference`: Optional artifact record ID, artifact ID, type ID, and digest.
- `revisionReference`: Optional desired-state revision ID.
- `idempotencyKey`: Stable key for the deployment intent.
- `createdAt`, `updatedAt`, `availableAt`, `expiresAt`: Lifecycle timestamps.
- `safePayloadJson`: Optional safe command metadata for runtime validation.

Rules:

- Commands are workspace-scoped.
- Commands do not contain raw artifact payloads, workflow definitions, credentials, tokens, or connection strings.
- Final command state is deterministic and cannot be overwritten by conflicting repeated calls.

## CommandLease

Time-limited claim for a command.

Fields:

- `leaseToken`: Opaque token returned to the claiming worker.
- `claimedBy`: Runtime worker ID.
- `claimedAt`: Claim timestamp.
- `leaseExpiresAt`: Lease expiration.
- `heartbeatAt`: Last heartbeat timestamp.
- `attemptNumber`: Processing attempt number.

Rules:

- Only one active lease may exist for a command.
- Runtime mutations require the current lease token.
- Expired leases cannot complete commands unless the platform explicitly accepts final reconciliation.

## CommandProgressEvent

Append-only runtime progress milestone.

Fields:

- `id`: Event ID.
- `commandId`: Command ID.
- `runId`: Linked run ID.
- `workspaceId`: Workspace ID.
- `status`: Progress status.
- `message`: Safe message.
- `percentComplete`: Optional percentage.
- `createdAt`: Event timestamp.

Rules:

- Progress events are safe and can be projected into deployment run history.
- Progress cannot move a final command back to running.

## CommandResult

Final runtime outcome.

Fields:

- `status`: Completed, failed, or rejected.
- `observedArtifactDigest`: Digest observed by the runtime.
- `runtimeReference`: Runtime-owned reference to the applied artifact/revision.
- `validationResult`: Safe validation summary.
- `applyResult`: Safe apply summary.
- `diagnostics`: Safe diagnostics.
- `completedAt`: Completion timestamp.

Rules:

- Result references are metadata only.
- Unsafe diagnostics are rejected or redacted.

## WebhookNotification

Non-authoritative command-available trigger.

Fields:

- `id`: Notification record ID.
- `workspaceId`: Workspace ID.
- `engineId`: Target engine.
- `commandId`: Command hint.
- `deliveryStatus`: Pending, sent, failed, or skipped.
- `createdAt`, `sentAt`: Delivery timestamps.
- `safePayloadJson`: Safe trigger payload.

Rules:

- Webhooks do not contain command payloads or secrets.
- Polling remains sufficient when webhooks fail.
