# Research: Custom Deployment Tiers

## Decision: Model custom tiers as workspace-owned definitions with platform-defined capability assignments

**Rationale**: The user requirement is explicitly analogous to roles and permissions: admins own the composition, while the platform owns the coded primitives. This lets workspaces use labels such as QA, UAT, Production EU, or Certification without forcing deployment logic to infer meaning from names.

**Alternatives considered**:

- Keep the fixed enum and add display aliases. Rejected because it cannot model multiple production-like tiers or customer-specific stage names cleanly.
- Store tiers as arbitrary strings. Rejected because deployment safeguards would depend on naming conventions.
- Let admins create custom capability IDs. Rejected because platform safety behavior needs stable semantics.

## Decision: Ship default tier definitions that map existing Dev, Test, Stage, and Production values

**Rationale**: Existing deployment environments must remain readable and operational. Default tier records provide a migration target and a usable setup path for new workspaces without requiring admins to configure tiers first.

**Alternatives considered**:

- Require admins to configure tiers before using deployments. Rejected because it breaks empty-state setup and increases adoption friction.
- Keep fixed enum and custom tiers side by side permanently. Rejected because two tier systems would complicate validation and UI behavior.

## Decision: Use coded tier capabilities as the only source for tier-aware behavior

**Rationale**: Current enum values are mostly display and sorting, but future deployment safeguards need stable semantics. Capabilities such as production-like, promotion-source, promotion-target, confirmation-required, rollback-enabled, secret-verification-required, and observability-required allow policies to be attached to custom tier labels.

**Alternatives considered**:

- Preserve a `Kind` enum behind every custom tier. Rejected as too coarse because one workspace may need several production-like or pre-production-like tiers with different operational rules.
- Use boolean columns for each behavior. Rejected because adding new semantics would require repeated schema and contract churn.

## Decision: Archive tiers instead of hard-deleting referenced tiers

**Rationale**: Environments and deployment history need stable references. Archiving hides tiers from new selections while preserving historical meaning and environment readability.

**Alternatives considered**:

- Allow deletion with cascading reassignment. Rejected because it can silently change historical meaning.
- Allow deletion only after manual reassignment. Still useful, but archive is the safer default and can be paired with a later cleanup flow.

## Decision: Require impact review for capability changes on used tiers

**Rationale**: Changing capability assignments can alter deployment safeguards for all environments using a tier. Admins should see affected environments and behavior changes before saving.

**Alternatives considered**:

- Allow immediate edits with only an audit record. Rejected because semantic changes can affect production-grade safety.
- Make capabilities immutable after tier creation. Rejected because admins need to refine tier behavior as their process evolves.

## Decision: Extend existing deployment workspace APIs and console feature area

**Rationale**: Tiers are a deployment setup concern and are consumed by existing deployment cockpit, environment setup, promotion preview, and run flows. Keeping contracts under deployment workspace routes avoids another workspace settings subsystem for a closely related domain.

**Alternatives considered**:

- Add a general workspace settings tier module. Rejected because these tiers have deployment-specific capabilities and safeguards.
- Keep tier management console-only and infer from environment creation. Rejected because API consumers and tests need the same server-authoritative model.
