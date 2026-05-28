# Platform Artifact Deployment Execution Plan

Status: draft execution plan

Last updated: 2026-05-28

## Product Model

Elsa Platform is an extensible deployment control plane for Elsa solutions. It is not the workflow runtime and does not execute workflows.

The settled architecture is:

```text
Producer integration
  -> submit immutable artifact

Elsa Platform
  -> registry, desired state, promotion, validation, deployment run, audit

Runtime/provider integration
  -> consume deployment command, apply artifact, report result
```

For the first-class Elsa workflow path:

- Elsa Studio remains the authoring and single-engine runtime inspection surface.
- Platform-integrated Studio uses **Submit to Platform** to create an immutable deployable artifact.
- **Submit to Platform** does not mean release, promotion, deployment, or immediate runtime execution.
- Elsa runtimes remain responsible for executing deployed artifacts and owning runtime state.
- Runtime integrations consume durable platform-owned deployment commands by pull/sync by default, webhook-triggered fetch where useful, or direct push where explicitly configured.

## Current Implementation Baseline

Completed or mostly complete:

- Identity and workspace tenancy: `specs/021-identity-tenancy` has 73 completed tasks and 1 remaining smoke-check task.
- Deployment UX and durable queued runs: `specs/022-deployment-ux` has 110 completed tasks and no open checklist tasks. It already includes durable deployment runs, confirmations, queue worker, rollback, runtime controls, permissions, persistence, API, and console flows.
- Engine health verification: `specs/023-engine-health-verification` has 38 completed tasks and 6 remaining verification/check tasks.

Partially implemented:

- Artifact registry: `specs/024-artifact-registry` has 31 completed tasks and 15 remaining tasks. Core/API/console implementation exists, but focused core, persistence, API, and refresh tests still need to be added/run.
- Custom deployment tiers: `specs/025-custom-deployment-tiers` has 46 completed tasks and 28 remaining tasks. Domain/API/persistence/console implementation is in progress, but test coverage, migration backfill, bounded-query verification, and final contract/quickstart updates remain.

Architectural delta introduced by the latest product decisions:

- The specs now describe immutable workflow artifacts, Studio **Submit to Platform**, platform-owned deployment commands, runtime pull/sync, webhook-triggered fetch, and direct push as transport alternatives.
- Existing code still primarily models deployment execution as platform-local durable queued runs with an in-process worker.
- There is not yet a first-class deployment command API for external runtime sync workers.
- There are not yet Studio or runtime integration NuGet packages.
- Existing desired-state records are structured platform records; they do not yet reference workflow artifacts as the main deployable input.

## Spec Work Needed

Update existing specs:

1. `specs/024-artifact-registry`
   - Extend from metadata-only registry toward typed artifact ingestion.
   - Keep catalog tables metadata/reference-only.
   - Add explicit artifact type and producer concepts where missing.
   - Align contracts with Studio submission as a future ingestion path.

2. `specs/022-deployment-ux`
   - Preserve first-slice queued runs.
   - Add follow-on tasks for deployment command records and runtime sync compatibility.
   - Move desired-state examples from inline workflow records toward artifact references where appropriate.

3. `specs/025-custom-deployment-tiers`
   - Finish current tier implementation.
   - Ensure promotion and deployment safeguards use tier capabilities, not tier names.
   - Keep environment technical capabilities separate from tier policy capabilities.

Create new focused specs:

1. `026-artifact-envelope-and-types`
   - Shared artifact envelope, artifact type IDs, payload reference model, digest rules, safe metadata, producer metadata, and target capability hints.
   - Decide whether this lives in existing `Elsa.Platform.Deployment.Artifacts` or a new shared package such as `Elsa.Platform.Artifacts`.

2. `027-studio-submit-to-platform`
   - NuGet package for Elsa Studio integration.
   - Adds **Submit to Platform** UX.
   - Packages workflow snapshots and safe metadata.
   - Authenticates to Platform and submits artifacts.
   - Keeps direct runtime Publish clearly separate or hidden for platform-integrated installations.

3. `028-runtime-command-sync`
   - Platform deployment command API.
   - Runtime sync worker contract.
   - Claim/lease, idempotency, expiration, progress reporting, stale command recovery, duplicate delivery behavior, and safe diagnostics.
   - Transport model: pull/sync first, webhook-triggered fetch second, direct push as explicit opt-in.

4. `029-workflow-artifact-runtime-applier`
   - NuGet package for Elsa Workflows runtime integration.
   - Advertises runtime capabilities.
   - Consumes workflow artifact deployment commands.
   - Verifies artifact digest and schema compatibility.
   - Installs workflow artifacts into the local runtime store.
   - Reports validation/apply results to Platform.

5. `030-artifact-backed-promotion`
   - Replace or augment structured workflow desired-state records with artifact references.
   - Promotion preview and deployment validation operate on artifacts plus environment-specific configuration.
   - Rollback redeploys known-good artifact-backed revisions.

## Execution Phases

### Phase 0: Documentation And Spec Alignment

Goal: make the repo narrative consistent before expanding implementation.

Tasks:

- Keep `README.md`, `specs/021-identity-tenancy`, `specs/022-deployment-ux`, and `specs/024-artifact-registry` aligned with the artifact/control-plane model.
- Add the new Spec Kit specs listed above.
- Update `docs/deployment-platform-phased-strategy.md` after the new specs exist, so it reflects the current platform direction rather than the older CLI-first framing.

Exit criteria:

- New specs are created with clear boundaries.
- Existing specs no longer imply that Platform queries Studio/runtime databases for workflow definitions.
- The terms **Submit to Platform**, artifact submission, deployment command, runtime sync worker, and webhook notification are used consistently.

### Phase 1: Close Current Open Work

Goal: stabilize the code already in flight before adding runtime sync.

Tasks:

- Run and record identity quickstart smoke checks for `021`.
- Complete remaining engine-health verification checks for `023`.
- Complete artifact-registry missing tests for core validation, EF persistence, API permissions/isolation, refresh behavior, and focused test runs.
- Complete custom-tier test coverage, migration backfill, bounded-query verification, quickstart updates, and final contract updates.

Exit criteria:

- `021`, `023`, `024`, and `025` task lists are closed or have explicit deferred items.
- Focused tests for workspace deployment, artifact registry, engine health, and tiers pass.
- `git diff --check` passes.

### Phase 2: Artifact Envelope And Registry Upgrade

Goal: make artifacts typed and producer-neutral.

Tasks:

- Add shared artifact envelope contracts.
- Add artifact type identifiers such as `elsa.workflow-definition`.
- Add producer metadata for Studio, CLI, CI, and manual registration.
- Add safe metadata schema for display/search without storing raw payload content in catalog tables.
- Update registry API and console to show artifact type, producer, compatibility hints, and submission status.

Exit criteria:

- Artifact records can represent a Studio-submitted workflow artifact without raw workflow content in catalog tables.
- Existing manual artifact registration remains compatible.
- Tests prove duplicate artifact identity, digest mismatch, unsafe metadata, and cross-workspace access fail closed.

### Phase 3: Deployment Command Contract

Goal: separate durable deployment intent from transport.

Tasks:

- Add deployment command records linked to deployment runs.
- Model command action, artifact/revision reference, target runtime, idempotency key, lease/claim state, expiration, progress, and safe diagnostics.
- Add API endpoints for runtime integrations to poll, claim, heartbeat, complete, fail, or reject commands.
- Keep existing in-process worker compatible by treating queued runs as an internal command consumer.
- Add duplicate poll/webhook tests proving no duplicate apply.

Exit criteria:

- Runtime pull can claim and complete commands without inbound runtime access.
- Stale claimed commands move to recovery-required or equivalent without automatic duplicate apply.
- Deployment history remains the console-facing source of truth.

### Phase 4: Studio Submit Integration

Goal: create the first artifact producer package.

Tasks:

- Create Studio integration package.
- Add **Submit to Platform** command and configuration.
- Package workflow definition snapshot, source IDs, display metadata, schema version, and content digest.
- Submit artifact metadata and payload reference/content according to the artifact envelope spec.
- Add user-facing states for submitted, failed, unauthorized, and duplicate artifact.

Exit criteria:

- A workflow authored in Studio can be submitted to Platform as an immutable artifact.
- Submission does not deploy or make the workflow immediately executable.
- Direct runtime Publish behavior is clearly separate when present.

### Phase 5: Runtime Sync And Workflow Artifact Applier

Goal: create the first artifact consumer package.

Tasks:

- Create Elsa Workflows runtime integration package.
- Add runtime capability registration/heartbeat.
- Add outbound sync worker for pending deployment commands.
- Fetch artifact metadata/payload, verify digest, validate runtime compatibility, and apply supported workflow artifacts.
- Report progress, validation result, apply result, observed digest, runtime reference, and safe diagnostics.

Exit criteria:

- Platform can deploy an `elsa.workflow-definition` artifact to a registered runtime via outbound runtime pull.
- Runtime does not need to expose an inbound endpoint.
- Failed validation/apply results are visible in Platform deployment history.

### Phase 6: Artifact-Backed Promotion And Rollback

Goal: make promotion/deployment flows artifact-first.

Tasks:

- Desired-state revisions reference workflow artifact versions instead of embedding workflow definition intent directly.
- Promotion preview compares artifact-backed revisions and environment-specific configuration.
- Tier capabilities drive safeguards.
- Environment/runtime capabilities drive technical compatibility.
- Rollback redeploys a known-good artifact-backed revision.

Exit criteria:

- Users can submit a workflow artifact, promote it across environments, deploy it to a runtime, and roll back from Platform.
- Platform remains agnostic about workflow internals beyond safe metadata, artifact type, digest, and compatibility hints.

## Immediate Next Actions

1. Finish `024-artifact-registry` tests and verification.
2. Finish `025-custom-deployment-tiers` tests, migration backfill, and verification.
3. Create `026-artifact-envelope-and-types`.
4. Create `028-runtime-command-sync` before implementing external runtime workers.
5. Create `027-studio-submit-to-platform` and `029-workflow-artifact-runtime-applier` once the envelope and command contracts are stable.

