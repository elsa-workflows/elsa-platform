# Data Model: Source-Scoped Catalog And Account Roadmap

## Package Source

Existing catalog source extended by visibility and ownership concepts over time.

**Current key fields**:

- `Id`
- `Name`
- `Type`
- `Url`
- `Enabled`
- `IncludePatterns`
- `ExcludePatterns`
- `ApprovalPolicy`
- `VersionDiscoveryPolicy`
- `Status`
- `SoftDeletedAt`

**Planned fields**:

- `Browseable`: whether the source can appear in public source selection.
- `OwnershipScope`: catalog-owned public source or workspace-owned source.
- `WorkspaceId`: owning workspace for custom feeds.

**Rules**:

- Public browsing can use only enabled, non-deleted, browseable, catalog-owned sources.
- Workspace-owned sources are visible only to authorized workspace members and operators.
- Feed URLs in public responses must be sanitized.

## Source-Qualified Package

The public identity for a package.

**Fields**:

- `SourceId`
- `PackageId`

**Rules**:

- Package details require both fields.
- Version identity is `SourceId + PackageId + Version`.
- Builder selections require `SourceId + PackageId + Version`.

## Account

Catalog-local person/customer principal.

**Fields**:

- `Id`
- `DisplayName`
- `PrimaryEmail`
- `CreatedAt`
- `UpdatedAt`

**Rules**:

- Accounts are linked to external identities.
- Accounts do not own package sources directly once workspaces exist.

## External Identity

Mapping from a verified external identity to a catalog account.

**Fields**:

- `Id`
- `AccountId`
- `Issuer`
- `Subject`
- `Provider`
- `Email`
- `CreatedAt`
- `LastSeenAt`

**Rules**:

- `Issuer + Subject` must be unique.
- Browser-supplied user IDs are not accepted as identity proof.
- Multiple external identities may link to the same account.

## Workspace

Ownership boundary for custom sources, members, and entitlements.

**Fields**:

- `Id`
- `Name`
- `OwnerAccountId`
- `CreatedAt`
- `UpdatedAt`

**Rules**:

- A new account receives a personal workspace by default.
- Workspace-owned sources are scoped to one workspace.
- Future central customer-service identifiers are external references, not primary keys.

## Workspace Member

Relationship between an account and a workspace.

**Fields**:

- `WorkspaceId`
- `AccountId`
- `Role`
- `CreatedAt`

**Rules**:

- Roles control source creation, source management, and browsing private workspace sources.

## Entitlement Snapshot

Catalog-local view of workspace capabilities and limits.

**Fields**:

- `WorkspaceId`
- `CanCreateCustomSources`
- `MaxSources`
- `MaxPackagesIndexed`
- `MaxVersionsPerPackage`
- `MaxSyncsPerDay`
- `PrivateFeedsEnabled`
- `Source`
- `SyncedAt`

**Rules**:

- Catalog source creation and indexing enforce the snapshot.
- Snapshots may initially be manually granted and later synchronized from Lovable, billing, or a central customer service.

## State Transitions

### Public Source

`configured -> enabled -> indexed -> browseable`

Disabled, deleted, or non-browseable sources are excluded from public selection.

### Workspace Source

`created -> pending sync -> indexed -> browseable to workspace`

Entitlement downgrade may move the source to disabled or over-limit state without deleting indexed history.
