# Data Model: Custom Deployment Tiers

## DeploymentTierDefinition

Workspace-owned profile that environments select instead of a fixed enum value.

Fields:

- `Id`: tier definition identifier.
- `WorkspaceId`: owning workspace.
- `Name`: display label, such as QA, UAT, Production EU, or Certification.
- `Description`: optional admin-facing explanation.
- `SortOrder`: workspace-defined display and selection order.
- `IsDefault`: indicates a system-created default tier for the workspace.
- `Status`: active or archived.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `CreatedByAccountId`, `UpdatedByAccountId`: optional customer actor metadata.
- `ArchivedAt`, `ArchivedByAccountId`: optional archive metadata.

Relationships:

- Belongs to one workspace.
- Has many `DeploymentTierCapabilityAssignment` records.
- Has many `DeploymentEnvironment` records through environment tier references.
- Has many `DeploymentTierChangeRecord` records.

Validation:

- Name is required.
- Active tier names are unique within a workspace.
- At least one active tier must remain available in a workspace.
- Referenced tiers cannot be hard-deleted.
- Archived tiers remain readable but cannot be assigned to new or edited environments.

## DeploymentTierCapability

Stable platform-defined semantic bit that can be attached to tier definitions.

Fields:

- `Id`: stable coded capability identifier.
- `Label`: human-readable capability label.
- `Description`: short explanation of the behavior implied by the capability.
- `Category`: grouping for display, such as classification, promotion, safeguards, validation, rollback, or observability.
- `IsDeprecated`: indicates a capability remains readable but should not be newly assigned.

Canonical initial capabilities:

- `deployment.tier.development-like`
- `deployment.tier.test-like`
- `deployment.tier.preproduction-like`
- `deployment.tier.production-like`
- `deployment.promotion.source`
- `deployment.promotion.target`
- `deployment.confirmation.required`
- `deployment.rollback.enabled`
- `deployment.secret-verification.required`
- `deployment.observability.required`

Relationships:

- Referenced by many `DeploymentTierCapabilityAssignment` records.

Validation:

- Workspace admins cannot create or rename capability IDs.
- Deprecated capabilities remain visible for existing assignments.
- Tier-aware deployment behavior must use capability IDs rather than tier names.

## DeploymentTierCapabilityAssignment

Association between a workspace tier definition and a coded capability.

Fields:

- `Id`: assignment identifier.
- `WorkspaceId`: owning workspace.
- `TierId`: parent tier definition.
- `CapabilityId`: coded capability identifier.
- `CreatedAt`: assignment timestamp.
- `CreatedByAccountId`: optional actor metadata.

Relationships:

- Belongs to one `DeploymentTierDefinition`.
- References one platform-defined `DeploymentTierCapability`.

Validation:

- Capability ID must exist in the platform-defined catalog.
- A tier cannot contain the same capability more than once.
- Assignment workspace must match the parent tier workspace.

## DeploymentEnvironment

Existing named deployment context that changes from fixed `Tier` enum semantics to a tier definition reference.

Fields affected by this feature:

- `TierId`: selected workspace tier definition.
- `LegacyTier`: optional temporary migration value for environments that have not yet been fully migrated.

Relationships:

- Belongs to one `DeploymentTierDefinition` in the same workspace.

Validation:

- Every environment must reference exactly one tier definition.
- Environment workspace and tier workspace must match.
- New or edited environments cannot select archived tiers.
- Existing environments assigned to archived tiers remain readable and may be reassigned to active tiers.

## DeploymentTierChangeRecord

Audit record for tier definition and capability changes.

Fields:

- `Id`: change record identifier.
- `WorkspaceId`: owning workspace.
- `TierId`: affected tier definition.
- `ActorAccountId`: account that made the change.
- `ChangeType`: created, renamed, reordered, capabilities changed, archived, restored, or deleted.
- `Summary`: safe human-readable change summary.
- `ChangedAt`: timestamp.
- `AffectedEnvironmentCount`: number of environments affected when known.

Relationships:

- Belongs to one workspace.
- References one tier definition.

Validation:

- Does not contain secrets, provider tokens, engine credentials, or raw desired-state payloads.
- Records semantic capability changes that could affect deployment safeguards.

## TierImpactSummary

Computed preview shown before saving capability changes on a tier used by environments.

Fields:

- `TierId`: tier being changed.
- `CurrentCapabilities`: current capability IDs.
- `ProposedCapabilities`: proposed capability IDs.
- `AddedCapabilities`: capabilities introduced by the change.
- `RemovedCapabilities`: capabilities removed by the change.
- `AffectedEnvironmentCount`: count of environments using the tier.
- `AffectedEnvironmentSamples`: bounded list of affected environment names and application names.
- `ChangedSafeguards`: list of safeguard behaviors that would change.

Validation:

- Generated before applying capability changes.
- Scoped to the caller's workspace.
- Contains safe summaries only.

## Default Tier Mapping

Default records created for workspaces without custom tier definitions.

Mappings:

- Dev: development-like, promotion-source.
- Test: test-like, promotion-source, promotion-target.
- Stage: preproduction-like, promotion-source, promotion-target, secret-verification-required.
- Production: production-like, promotion-target, confirmation-required, rollback-enabled, secret-verification-required, observability-required.

Validation:

- Existing Dev/Test/Stage/Production environments map to these defaults.
- Defaults can be renamed or reordered by admins after creation.
- Default capability assignments can be edited with the same impact review as custom tiers.
