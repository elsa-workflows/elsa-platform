# Research: Dynamic Desired-State Requirements

## Decision: Compute Requirements From Tier Capabilities

Desired-state requirement visibility will be derived from the current environment's tier capability IDs.

**Rationale**: Custom tiers already define stable platform semantics. Validation already uses capabilities such as `deployment.observability.required`; reusing them prevents UI rules from drifting from deployment validation.

**Alternatives considered**:

- Hardcode by tier name: rejected because custom tiers intentionally remove fixed-name semantics.
- Keep always-visible optional controls: rejected because it confuses users on Dev/Test.
- Store requirement assignments separately: rejected for v1 because the current capability model is sufficient.

## Decision: Backend Requirement Catalog Is The Source Of Truth

Deployment core will expose stable desired-state requirement metadata through the workspace API.

**Rationale**: The backend already owns tier capabilities and validation. Frontend-only mapping would reintroduce drift and make future record kinds harder to govern.

**Alternatives considered**:

- Frontend local constants only: rejected because validation IDs and tier capabilities already live in backend code.
- Persisted requirement table: rejected because requirements are platform-defined semantic mappings for this iteration.

## Decision: Contextual Fixes Use Explicit Query Intent

Validation links can request a supported editor, such as observability, through a clear query parameter.

**Rationale**: This preserves a simple deep-link path from validation blockers without showing production-only controls on every source revision page.

**Alternatives considered**:

- Always show advanced optional sections: rejected because it returns to the original confusing UX.
- Require users to manually add records through a generic builder first: rejected because the current UI only supports observability as a guided record.
