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

Completed:

- Identity and workspace tenancy: `specs/021-identity-tenancy` has all tasks completed. Focused smoke coverage is recorded in the quickstart; live external provider browser sign-in remains environment-specific.
- Deployment UX and durable queued runs: `specs/022-deployment-ux` has 110 completed tasks and no open checklist tasks. It already includes durable deployment runs, confirmations, queue worker, rollback, runtime controls, permissions, persistence, API, and console flows.
- Engine health verification: `specs/023-engine-health-verification` has all tasks completed. Core, persistence, API, console, and typecheck results are recorded in the quickstart.
- Artifact registry: `specs/024-artifact-registry` has all tasks completed. Core, persistence, API, and console focused checks are recorded in the quickstart.
- Custom deployment tiers: `specs/025-custom-deployment-tiers` has all tasks completed. Tier capability semantics, migration backfill, bounded-query verification, and console/API coverage are recorded in the quickstart.
- Artifact envelope and types: `specs/026-artifact-envelope-and-types` has all tasks completed. The envelope/type contracts live in `Elsa.Platform.Deployment.Artifacts`, and the workspace artifact registry now stores type, producer, safe display metadata, compatibility hints, and payload references.
- Runtime command sync: `specs/028-runtime-command-sync` now has backend command models, EF persistence, SQLite/SQL Server migrations, runtime pull/sync APIs, claim/lease behavior, heartbeat/progress/completion reporting, stale recovery, idempotency, webhook notification records, in-process worker compatibility, and focused core/persistence/API coverage.

Architectural delta introduced by the latest product decisions:

- The specs now describe immutable workflow artifacts, Studio **Submit to Platform**, platform-owned deployment commands, runtime pull/sync, webhook-triggered fetch, and direct push as transport alternatives.
- Deployment execution now creates platform-owned command records linked to deployment runs while keeping the platform-local in-process worker compatible.
- External runtime sync workers can poll, claim, heartbeat, progress, complete, fail, or reject commands through the runtime command API.
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

Focused specs:

1. `026-artifact-envelope-and-types`
   - Completed. Shared artifact envelope, artifact type IDs, payload reference model, digest rules, safe metadata, producer metadata, and target capability hints live in `Elsa.Platform.Deployment.Artifacts`.

2. `027-studio-submit-to-platform`
   - NuGet package for Elsa Studio integration.
   - Adds **Submit to Platform** UX.
   - Packages workflow snapshots and safe metadata.
   - Authenticates to Platform and submits artifacts.
   - Keeps direct runtime Publish clearly separate or hidden for platform-integrated installations.

3. `028-runtime-command-sync`
   - Backend implementation completed for platform deployment command records, runtime pull/sync API, claim/lease, idempotent completion, progress reporting, stale recovery, safe diagnostics, and webhook notification records.
   - Remaining follow-on work belongs with runtime credential hardening/provider transports and the concrete workflow runtime applier package.

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

- Run and record identity quickstart smoke checks for `021`. Completed.
- Complete remaining engine-health verification checks for `023`. Completed.
- Complete artifact-registry missing tests for core validation, EF persistence, API permissions/isolation, refresh behavior, and focused test runs. Completed.
- Complete custom-tier test coverage, migration backfill, bounded-query verification, quickstart updates, and final contract updates. Completed.

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

- Add deployment command records linked to deployment runs. Completed.
- Model command action, artifact/revision reference, target runtime, idempotency key, lease/claim state, expiration, progress, and safe diagnostics. Completed.
- Add API endpoints for runtime integrations to poll, claim, heartbeat, complete, fail, or reject commands. Completed.
- Keep existing in-process worker compatible by treating queued runs as an internal command consumer. Completed.
- Add duplicate poll/claim and stale-recovery tests proving no duplicate apply. Completed for core, persistence, and API claim lifecycle; provider webhook delivery remains a future transport concern.

Exit criteria:

- Runtime pull can claim and complete commands without inbound runtime access. Completed.
- Stale claimed commands move to recovery-required without automatic duplicate apply. Completed.
- Deployment history remains the console-facing source of truth. Completed through command event projection into run history.

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

1. Implement `028-runtime-command-sync` before implementing external runtime workers.
2. Create `027-studio-submit-to-platform` and `029-workflow-artifact-runtime-applier` once the envelope and command contracts are stable.
