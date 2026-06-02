# Data Model: Organization Tenancy

## Organization

Root customer tenant boundary.

Fields:

- `Id`: organization identifier.
- `Name`: user-visible organization name.
- `Slug`: optional stable display/navigation slug.
- `Status`: active, archived, or suspended.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `ArchivedAt`: optional archive timestamp.
- `CreatedByAccountId`: optional creating account.
- `CustomerReference`: optional external customer/billing reference.

Relationships:

- Has many `OrganizationMembership` records.
- Has many `Workspace` records.
- Has zero or more `OrganizationEntitlementSnapshot` records, with the latest snapshot authoritative.
- Has many `OrganizationAuditRecord` records.

Validation:

- Name is required.
- Active organization names do not need to be globally unique.
- Archived organizations cannot create new workspaces or mutate customer-owned resources except through recovery/admin flows.

## OrganizationMembership

Relationship between an account and an organization.

Fields:

- `Id`: membership identifier.
- `OrganizationId`: organization.
- `AccountId`: account.
- `Role`: owner, administrator, billing administrator, workspace creator, member, or reader.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `DisabledAt`: optional disabled timestamp.
- `InvitedByAccountId`: optional invitation or grant actor.

Relationships:

- Belongs to one organization.
- Belongs to one account.

Validation:

- `(OrganizationId, AccountId)` is unique for active membership.
- At least one active owner must remain for an active organization.
- Organization membership alone does not authorize workspace-owned resource access.

## Workspace

Operational isolation boundary inside one organization.

Fields affected by this feature:

- `OrganizationId`: owning organization.
- `Name`: workspace display name.
- `Status`: active or archived.
- `Kind`: retained only for compatibility until deprecated.
- `CreatedAt`, `UpdatedAt`, `SoftDeletedAt`: audit timestamps.

Relationships:

- Belongs to one organization.
- Has many `WorkspaceMembership` records.
- Has many workspace-owned resources.

Validation:

- Every active workspace must have exactly one organization.
- Workspace names are unique among active workspaces in the same organization.
- Workspace-owned records remain isolated by workspace.

## WorkspaceMembership

Relationship between an account and a workspace.

Fields:

- `Id`: membership identifier.
- `WorkspaceId`: workspace.
- `AccountId`: account.
- `Role`: owner, source administrator, deployment administrator, deployer, reader, or feature-defined role.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `DisabledAt`: optional disabled timestamp.

Relationships:

- Belongs to one workspace.
- Belongs to one account.
- Effective only when the account also has active organization membership for the workspace's organization.

Validation:

- `(WorkspaceId, AccountId)` is unique for active membership.
- Workspace reads require active workspace membership unless a specific audited organization role grants workspace-wide access.
- Workspace mutations require operation-specific role or permission grants.

## OrganizationEntitlementSnapshot

Latest server-enforced capability and limit state for an organization.

Fields:

- `Id`: entitlement snapshot identifier.
- `OrganizationId`: organization.
- `CanCreateCustomSources`: custom-source capability.
- `MaxSources`: maximum active custom sources across the organization unless overridden.
- `MaxWorkspaces`: maximum active workspaces.
- `MaxPackagesIndexed`: optional package index limit.
- `MaxVersionsPerPackage`: optional version limit.
- `MaxSyncsPerDay`: optional sync-rate limit.
- `PrivateFeedsEnabled`: credentialed private-feed capability.
- `ManagedHostingEnabled`: managed-hosting capability.
- `DeploymentTargetsEnabled`: BYOC/deployment-target capability.
- `SyncedAt`, `CreatedAt`, `UpdatedAt`: audit timestamps.

Validation:

- Missing snapshot means entitlement-gated organization actions are denied.
- Latest snapshot is authoritative for new entitlement-gated operations.
- Limits are non-negative.

## OrganizationAuditRecord

Safe metadata record for organization-level changes.

Fields:

- `Id`: audit record identifier.
- `OrganizationId`: organization.
- `ActorAccountId`: customer actor when available.
- `OperatorSubject`: operator actor when applicable.
- `Action`: membership changed, workspace created, workspace archived, entitlement changed, organization archived, or role changed.
- `TargetType`: organization, workspace, membership, entitlement, or role.
- `TargetId`: target identifier.
- `Summary`: safe human-readable summary.
- `CreatedAt`: audit timestamp.

Validation:

- Does not contain secrets, provider tokens, raw credentials, or raw deployment payloads.
- Distinguishes customer-authorized changes from operator-authorized changes.

## State Transitions

First sign-in:

```text
trusted identity received -> external identity not found -> create account -> create organization -> create default workspace -> create organization owner membership -> create workspace owner membership -> return organization context
```

Returning sign-in:

```text
trusted identity received -> external identity found -> update profile metadata and last seen -> return active organization memberships and visible workspace memberships
```

Organization workspace creation:

```text
organization admin -> verify organization role -> verify organization entitlement/limits -> create workspace -> create requested workspace memberships -> audit change
```

Workspace-scoped request:

```text
authenticated customer -> resolve account -> resolve workspace organization -> verify organization membership -> verify workspace membership/permission -> verify entitlement if needed -> execute operation
```

Migration:

```text
existing workspace -> create owning organization -> assign workspace organization id -> create organization memberships from workspace owners/members -> preserve workspace memberships and resource ownership
```
