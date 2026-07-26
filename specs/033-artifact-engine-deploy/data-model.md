# Data Model: Artifact To Engine Deployment

## DeployabilityResult

Evaluation of one desired-state revision against one target engine.

Fields:

- `workspaceId`: Tenant boundary.
- `revisionId`: Desired-state revision being evaluated.
- `environmentId`: Revision target environment.
- `targetEngineId`: Selected engine.
- `mode`: Dry-run/apply/deploy mode.
- `status`: Deployable, warning, or blocked.
- `evaluatedAt`: Timestamp for capability freshness and UI display.
- `artifactResults`: Per-artifact deployability results.
- `blockers`: Aggregated deployment blockers.

Rules:

- Evaluation must not mutate platform or runtime state.
- Engine must belong to the revision's target environment unless a future cross-environment flow explicitly authorizes otherwise.
- Any blocked artifact blocks the whole deployment action.
- Missing or stale engine capability metadata is a blocker.

## ArtifactDeployabilityResult

Deployability status for one artifact record in the revision.

Fields:

- `artifactRecordId`: Workspace artifact record ID.
- `recordName`: Desired-state record name.
- `artifactId`: Stable artifact identity.
- `artifactTypeId`: Artifact type such as `elsa.workflow-definition`.
- `artifactSchemaVersion`: Artifact schema version.
- `contentDigest`: Expected content digest.
- `status`: Deployable, warning, or blocked.
- `requiredCapabilities`: Canonical capability IDs required by the artifact.
- `missingCapabilities`: Required capabilities absent from the engine.
- `payloadAvailable`: Whether Valence Control can open a safe download stream.
- `diagnostics`: Safe structured details.

Rules:

- Artifact record ID, type, identity, and digest must match the desired-state reference.
- Archived artifacts, invalid inspection state, unavailable payloads, and unsupported types block deployment.
- Artifact-declared compatibility hints are authoritative when present; artifact type defaults are fallback.
- Default apply capability IDs use `artifact.{artifactTypeId}.apply`.

## ArtifactApplyRequirement

Normalized requirement derived from an artifact record and artifact type registry.

Fields:

- `artifactTypeId`: Required artifact type.
- `artifactSchemaVersion`: Required schema version.
- `runtimeFamily`: Runtime family constraint, if declared.
- `runtimeVersionRange`: Runtime version range, if declared.
- `requiredCapabilities`: Canonical capability IDs.
- `source`: CompatibilityHint or ArtifactTypeDefault.

Rules:

- Legacy short apply capabilities may be recognized as aliases, but stored/displayed requirements use canonical IDs.
- Unknown artifact types block deployment unless the registry defines them before queueing.

## EngineApplyCapability

Runtime-advertised capability metadata for a registered engine.

Fields:

- `engineId`: Registered workflow engine.
- `environmentId`: Owning environment.
- `capabilityId`: Machine-readable capability ID.
- `label`: Human-readable label.
- `boundary`: Capability boundary.
- `artifactTypeId`: Optional artifact type supported by this capability.
- `supportedSchemaVersions`: Optional explicit schema versions/ranges.
- `reportedAt`: Registration or heartbeat timestamp.

Rules:

- Capabilities are workspace-scoped through their engine.
- Capability metadata must be current for deployment.
- Canonical apply capabilities must be advertised as `artifact.{artifactTypeId}.apply`.

## ArtifactPayloadAccess

Safe instruction for retrieving an artifact payload.

Fields:

- `artifactRecordId`: Artifact to retrieve.
- `downloadUrl`: Valence Control URL, not a raw storage path.
- `fileName`: Safe file name.
- `contentType`: MIME type.
- `contentDigest`: Expected digest.
- `expiresAt`: Optional access expiry.
- `audience`: ConsoleUser or RuntimeCommandLease.

Rules:

- Console downloads require deployment read access.
- Runtime downloads require the active lease for the command that references the artifact.
- Raw local paths and provider tokens are never displayed as the deployment interface.

## DeploymentCommandPayload

Safe runtime command body for one revision deploy intent.

Fields:

- `commandId`: Runtime command ID.
- `workspaceId`: Tenant boundary.
- `runId`: Deployment run.
- `revisionId`: Desired-state revision.
- `environmentId`: Target environment.
- `engineId`: Target engine.
- `action`: Deploy, rollback, or validate.
- `mode`: DryRun or Apply.
- `idempotencyKey`: Stable key for revision/engine/mode.
- `artifacts`: Ordered list of `DeploymentCommandArtifactItem`.

Rules:

- Exactly one command is created per approved revision, target engine, and mode.
- Payload contains safe metadata and platform download instructions only.
- The command is idempotent for duplicate queue attempts and final-state retries.

## DeploymentCommandArtifactItem

One artifact item inside a deployment command.

Fields:

- `artifactRecordId`: Workspace artifact record ID.
- `artifactId`: Artifact identity.
- `artifactTypeId`: Artifact type.
- `artifactSchemaVersion`: Artifact schema version.
- `contentDigest`: Expected digest.
- `displayName`: Safe display label.
- `downloadUrl`: Lease-scoped runtime download route.
- `status`: Pending, downloading, validated, applying, applied, failed, rejected, or skipped.
- `runtimeReference`: Optional runtime-owned reference after apply.
- `observedDigest`: Digest observed by runtime.
- `diagnostics`: Safe diagnostics for this artifact item.

Rules:

- Runtime must verify the digest before apply.
- Per-artifact final outcomes must be preserved when aggregate command finalizes.

## RuntimeApplyReport

Runtime-submitted progress or final result for a command.

Fields:

- `leaseToken`: Active command lease token.
- `workerId`: Runtime worker identity.
- `status`: Running, completed, failed, rejected, or recovery-required.
- `percentComplete`: Optional aggregate progress.
- `message`: Safe progress message.
- `artifacts`: Per-artifact apply outcomes.
- `diagnostics`: Aggregate safe diagnostics.

Rules:

- Mutations require the current unexpired lease token.
- Unsafe messages are rejected or redacted.
- A failed item after a successful item marks the run failed and requires explicit operator recovery.

## DeploymentBlocker

User-facing reason that prevents deployment.

Fields:

- `id`: Stable blocker ID.
- `severity`: Warning or blocker.
- `scope`: Artifact, engine, payload, permission, tier, confirmation, or command.
- `message`: Safe explanation.
- `remediation`: Suggested action such as install runtime applier, refresh heartbeat, restore artifact, register compatible engine, or fix payload access.
- `artifactRecordId`: Optional related artifact.
- `engineId`: Optional related engine.

Rules:

- Blockers must be safe for console display and run history.
- Blockers must be distinct for missing capability, stale engine metadata, unavailable payload, digest mismatch, unsupported schema, permissions, and tier requirements.
