# Research: Saved Runtime Configurations

## Decision: Store Builder Intent As Versioned JSON Plus Queryable Metadata

Rationale: Runtime intent will evolve as planner and deployment targets mature. JSON preserves shape while top-level workspace/name/status fields support listing and authorization.

Alternatives considered: fully normalized schema, rejected for first slice because intent shape is still evolving.

## Decision: Explicit Version Snapshots

Rationale: Explicit snapshots avoid noisy version history and match the PRD recommendation.

Alternatives considered: automatic version on every save, rejected as too noisy.

## Decision: Workspace-Owned Records

Rationale: Existing account/workspace custom feed foundation is the right ownership boundary and avoids separate user/project models for MVP.

Alternatives considered: global user-owned configurations, rejected because paid/team features need workspace ownership.
