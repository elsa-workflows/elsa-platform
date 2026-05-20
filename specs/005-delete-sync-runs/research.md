# Research: Delete Sync Runs

## Decision: Manual cleanup first, with explicit UTC cutoff

**Rationale**: The feature request is about avoiding excessive growth from stale information, but the safest first capability is administrator-initiated cleanup. Requiring an explicit cutoff preserves recent troubleshooting history by default and avoids surprising retention behavior.

**Alternatives considered**:

- **Automatic scheduled retention**: Deferred because it introduces policy, configuration, and operational surprise beyond the requested first capability.
- **Hard-coded retention period**: Rejected because operators may need different troubleshooting windows across environments.
- **Delete all history button**: Rejected because it is too easy to erase useful recent diagnostics.

## Decision: Only terminal sync runs are eligible

**Rationale**: The existing `SyncRunStatus` values make `Running` the only current non-terminal state. Cleanup should centralize terminal-state logic so future non-terminal statuses, such as queued, can be excluded without changing endpoint behavior.

**Alternatives considered**:

- **Allow deletion of running runs**: Rejected because active sync diagnostics could disappear while the sync service still references them.
- **Block deletion of failed runs**: Rejected because failed historical runs can be the largest source of stale diagnostics; they are safe to delete once obsolete.

## Decision: Delete sync run headers and item diagnostics only

**Rationale**: Sync run records are operational history. Package sources, packages, versions, manifests, validation results, approvals, and listing state are current catalog state and must remain intact. Existing EF mapping already cascades `SyncRun -> SyncRunItem` and uses set-null for `SyncRunItem -> PackageVersion`, matching this boundary.

**Alternatives considered**:

- **Soft-delete sync runs**: Rejected because it does not solve database growth.
- **Delete related package versions when indexed by a deleted run**: Rejected because package versions are durable catalog state and must remain immutable.
- **Archive deleted sync runs**: Rejected for first version because it moves growth elsewhere and adds infrastructure without current product need.

## Decision: Preview bulk deletion before executing it

**Rationale**: Operators need to know the impact of a cutoff before deleting potentially large history. A preview that reports eligible and excluded counts also gives the UI a safe confirmation step.

**Alternatives considered**:

- **Immediate bulk delete from cutoff only**: Rejected because the administrator cannot validate scope before destructive cleanup.
- **Preview only in frontend**: Rejected because server-side counts are authoritative and avoid duplicating eligibility rules.

## Decision: Use existing admin authorization and operational logs

**Rationale**: Sync cleanup belongs to the protected admin surface, and the current feature does not require a separate audit store. Structured operational logs with actor when available, scope, cutoff, and counts are sufficient for accountability in v1.

**Alternatives considered**:

- **New cleanup audit table**: Rejected because the spec explicitly avoids compliance-grade archiving and no new persistent entity is needed.
- **Unauthenticated maintenance endpoint**: Rejected because deletion is an administrative operation.
