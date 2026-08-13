# Research: Artifact To Engine Deployment

## Decision 1: Deployability is a first-class Deployment Core service

**Decision**: Add a structured deployability service/model in `ValenceControl.Deployment.Core.Workspace` and expose it through workspace-scoped API endpoints before queueing a run.

**Rationale**: Current queue-time validation throws exceptions such as missing runtime capability text after the user clicks deploy. A deployability result gives the console stable, testable blocker data with remediation actions and keeps queueing logic from becoming the only validation surface.

**Alternatives Considered**:

- Keep validation inside `DeploymentRunService.QueueDeploymentAsync` only. Rejected because it cannot power preflight UI and produces unstructured exception strings.
- Put deployability entirely in the console. Rejected because compatibility and workspace isolation must be authoritative server-side.

## Decision 2: Canonical artifact apply capabilities replace short apply capability defaults

**Decision**: Derive default apply requirements as `artifact.{artifactTypeId}.apply`, for example `artifact.elsa.workflow-definition.apply`. Existing short hints such as `workflow-definition.apply` are treated as legacy aliases during migration/normalization but new registry defaults and UI blockers use the canonical form.

**Rationale**: Canonical IDs are stable, namespaced by artifact type, and prevent ambiguous capability names as more artifact types are added.

**Alternatives Considered**:

- Continue using `workflow-definition.apply`. Rejected because it does not generalize cleanly to multiple artifact types.
- Store only broad runtime family support. Rejected because runtimes need to advertise concrete apply operations, not just families.

## Decision 3: One runtime command contains all artifact records for the revision

**Decision**: Queue exactly one deployment command for an approved revision, target engine, and mode. The command payload contains a list of artifact items with identity, type, schema, expected digest, safe display metadata, download instruction, and per-artifact status.

**Rationale**: One command preserves the user's deployment intent and keeps run history aligned with the revision. Per-artifact items still let the runtime report granular validation/apply outcomes.

**Alternatives Considered**:

- One command per artifact. Rejected because partial command ordering and duplicate run history would make a single revision deploy harder to reason about.
- One command per applier. Rejected because applier ownership is runtime implementation detail and should not leak into Valence Control dispatch.

## Decision 4: Runtime artifact downloads use command lease authorization

**Decision**: Add a runtime-facing artifact download route under the runtime command API. It requires the current command lease token, worker identity, target engine ownership, non-expired lease, and association between the command payload and requested artifact.

**Rationale**: Console artifact downloads and runtime payload fetches have different trust boundaries. Runtime fetches should prove the worker has claimed the exact command that needs the artifact.

**Alternatives Considered**:

- Reuse the console `/artifacts/{id}/download` endpoint for runtimes. Rejected because deployment read permission alone is too broad for worker payload access.
- Embed direct storage URLs or local paths in commands. Rejected because commands must not expose provider credentials or filesystem details.

## Decision 5: Partial apply is a failed run with explicit recovery

**Decision**: When one artifact applies successfully and a later artifact fails, the command and run are marked failed or recovery-required according to finalization rules, with per-artifact outcomes preserved. The system does not claim automatic rollback.

**Rationale**: The runtime may have changed local state before failure. Valence Control cannot safely infer rollback semantics across artifact types without runtime-specific recovery logic.

**Alternatives Considered**:

- Automatically rollback successful artifact items. Rejected because Valence Control does not own runtime-local apply state and rollback may not exist.
- Mark the run succeeded with warnings. Rejected because the revision was not fully applied.

## Decision 6: Stale or missing engine capability metadata blocks deployment

**Decision**: Deployability treats missing capability metadata, stale heartbeat/capability timestamps, and environment mismatches as blockers before queueing.

**Rationale**: Deploying on unknown runtime capabilities creates avoidable runtime failures and poor operator guidance. Blocking early gives a concrete remediation path: reconnect or refresh the target engine.

**Alternatives Considered**:

- Allow queueing and let runtime reject. Rejected because it delays a known setup issue until after command creation.
- Show a warning only. Rejected because the clarified requirements state stale or missing capability metadata must block deployment.

## Decision 7: Runtime command completion accepts per-artifact outcomes

**Decision**: Extend runtime progress/finalization contracts to carry per-artifact apply outcomes alongside the aggregate command status, observed digests, runtime references, and safe diagnostics.

**Rationale**: Operators need to know which artifact failed and what, if anything, applied before failure. Aggregate command diagnostics are insufficient for multi-artifact revisions.

**Alternatives Considered**:

- Store outcomes only as text diagnostics. Rejected because diagnostics are hard to query and cannot reliably power UI state.
- Add separate command records for outcomes. Rejected for the first implementation because serialized safe outcome metadata on the command/run is enough and avoids extra lifecycle complexity.
