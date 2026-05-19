# Data Model: Account-Owned Custom Feeds

## Account

Represents a durable catalog-local user account.

Fields:

- `Id`: catalog account identifier.
- `DisplayName`: optional latest display name from external identity.
- `Email`: optional latest email from external identity.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Has many `ExternalIdentity` records.
- Has many `WorkspaceMembership` records.

Validation:

- Account is created only from a trusted identity context.
- Email/display name changes update metadata but do not change account identity.

## ExternalIdentity

Maps an external sign-in to a catalog account.

Fields:

- `Id`: identity record identifier.
- `AccountId`: owning account.
- `Issuer`: trusted identity issuer.
- `Subject`: trusted subject within issuer.
- `DisplayName`: optional latest display name.
- `Email`: optional latest email.
- `LastSeenAt`: most recent successful identity use.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Belongs to one `Account`.

Validation:

- `(Issuer, Subject)` must be unique.
- `Issuer` and `Subject` are required and normalized by trimming whitespace.
- Browser-supplied account IDs are never accepted as identity.

## Workspace

Container for sources, members, and entitlement enforcement.

Fields:

- `Id`: workspace identifier.
- `Name`: user-visible workspace name.
- `Kind`: personal initially; organization-ready later.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `SoftDeletedAt`: optional deletion marker.

Relationships:

- Has many `WorkspaceMembership` records.
- Has many workspace-owned `PackageSource` records.
- Has zero or one latest `WorkspaceEntitlementSnapshot`.

Validation:

- New accounts receive exactly one personal workspace.
- Soft-deleted workspaces cannot create sources or expose private packages.

## WorkspaceMembership

Connects an account to a workspace.

Fields:

- `Id`: membership identifier.
- `WorkspaceId`: workspace.
- `AccountId`: account.
- `Role`: owner, source administrator, or reader.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Belongs to one `Workspace`.
- Belongs to one `Account`.

Validation:

- `(WorkspaceId, AccountId)` must be unique.
- Source creation requires owner or source administrator role.
- Package/source browsing requires any active membership.

## WorkspaceEntitlementSnapshot

Latest catalog-enforced capability snapshot for a workspace.

Fields:

- `Id`: snapshot identifier.
- `WorkspaceId`: workspace.
- `CanCreateCustomSources`: whether custom source creation is allowed.
- `MaxSources`: maximum active workspace-owned sources.
- `MaxPackagesIndexed`: optional package limit for future sync enforcement.
- `MaxVersionsPerPackage`: optional version limit for future sync enforcement.
- `MaxSyncsPerDay`: optional sync-rate limit for future enforcement.
- `PrivateFeedsEnabled`: whether credentialed private feeds are allowed; initially false.
- `SyncedAt`: when the snapshot was produced or manually granted.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Belongs to one `Workspace`.

Validation:

- Catalog enforces latest snapshot before creating a workspace-owned source.
- Missing snapshot means custom source creation is denied.
- `MaxSources` must be non-negative.

## PackageSource Ownership Extension

Existing `PackageSource` gains ownership metadata.

Fields:

- `OwnerWorkspaceId`: optional workspace owner. Null means catalog-owned source.
- `Visibility`: public or workspace.

Relationships:

- Catalog-owned public sources have no workspace owner.
- Workspace-owned sources belong to one workspace.

Validation:

- Workspace-owned sources are private by default.
- Workspace-owned source creation requires entitlement.
- Public anonymous APIs include only catalog-owned public browseable sources.
- Workspace APIs include catalog-owned public browseable sources plus sources owned by the selected workspace.

## State Transitions

Account provisioning:

```text
trusted identity received -> external identity not found -> create account -> create personal workspace -> create owner membership -> return workspace context
trusted identity received -> external identity found -> update profile metadata -> return existing memberships
```

Custom source creation:

```text
draft request -> validate membership -> validate entitlement -> validate source -> persist workspace-owned disabled/enabled source -> source available for sync
```

Entitlement update:

```text
operator grant -> replace latest snapshot -> future source creation uses latest limits
```
