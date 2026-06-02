# Data Model: Identity And Workspace Tenancy

> **Forward compatibility note**: `specs/031-organization-tenancy` extends this model with `Organization`, `OrganizationMembership`, and organization-scoped entitlements. The `Workspace` model remains valid as an operational boundary but is no longer the root customer tenant boundary.

## TrustedIdentityContext

Represents a verified customer sign-in or trusted server-to-server user context.

Fields:

- `Issuer`: trusted identity issuer.
- `Subject`: stable subject within the issuer.
- `DisplayName`: optional latest display name.
- `Email`: optional latest email.
- `AuthenticationMethod`: customer token, browser login session, or trusted backend adapter.
- `Provider`: configured platform identity provider adapter that produced the context.

Validation:

- Issuer and subject are required.
- Issuer and subject are normalized before lookup.
- Email and display name are profile metadata only and never form account identity.
- Browser-supplied account IDs, workspace IDs, roles, or entitlements are ignored as authority.

## PlatformIdentityProvider

Represents a configured identity adapter such as Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integration.

Fields:

- `Provider`: adapter kind.
- `Authority`: provider metadata authority when using OIDC discovery.
- `Audience`: expected token audience.
- `Issuer`: expected issuer when explicitly configured.
- `Claims`: subject, display name, and email claim mapping.

Validation:

- Provider adapters must produce issuer and subject.
- Provider adapters may produce email and display name as metadata only.
- Provider adapters do not produce authoritative workspace roles or entitlements.

## Account

Durable platform-local customer account.

Fields:

- `Id`: account identifier.
- `DisplayName`: optional latest display name.
- `Email`: optional latest email.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Has many `ExternalIdentity` records.
- Has many `WorkspaceMembership` records.

Validation:

- Created only from a trusted identity context.
- Profile metadata can change without changing account identity.

## ExternalIdentity

Stable mapping from external sign-in to a platform account.

Fields:

- `Id`: external identity identifier.
- `AccountId`: owning account.
- `Issuer`: trusted issuer.
- `Subject`: trusted subject.
- `DisplayName`: optional latest display name.
- `Email`: optional latest email.
- `LastSeenAt`, `CreatedAt`, `UpdatedAt`: audit timestamps.

Validation:

- `(Issuer, Subject)` is unique.
- Same email under different issuer/subject pairs does not automatically merge accounts.

## Workspace

Primary resource boundary for customer-owned platform data in this slice; `specs/031-organization-tenancy` adds Organization as the root customer tenant boundary.

Fields:

- `Id`: workspace identifier.
- `Name`: user-visible workspace name.
- `Kind`: personal initially, organization-ready later.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `SoftDeletedAt`: optional deletion marker.

Relationships:

- Has many `WorkspaceMembership` records.
- Has many customer-owned resources.
- Has zero or more entitlement snapshots, with the latest snapshot authoritative.

Validation:

- First customer sign-in creates one personal workspace.
- Soft-deleted workspaces cannot expose or mutate private customer-owned resources.

## WorkspaceMembership

Relationship between an account and a workspace.

Fields:

- `Id`: membership identifier.
- `WorkspaceId`: workspace.
- `AccountId`: account.
- `Role`: owner, source administrator, deployer, reader, or future organization role.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `DisabledAt`: optional future membership disable marker.

Validation:

- `(WorkspaceId, AccountId)` is unique for active membership.
- Workspace reads require active membership.
- Workspace mutations require the role needed by the operation.

## WorkspaceEntitlementSnapshot

Latest server-enforced capability and limit state for a workspace.

Fields:

- `Id`: entitlement snapshot identifier.
- `WorkspaceId`: workspace.
- `CanCreateCustomSources`: custom-source capability.
- `MaxSources`: active custom-source limit.
- `MaxPackagesIndexed`: optional package index limit.
- `MaxVersionsPerPackage`: optional version limit.
- `MaxSyncsPerDay`: optional sync-rate limit.
- `PrivateFeedsEnabled`: credentialed private-feed capability.
- `ManagedHostingEnabled`: future managed-hosting capability.
- `DeploymentTargetsEnabled`: future BYOC/deployment-target capability.
- `SyncedAt`, `CreatedAt`, `UpdatedAt`: audit timestamps.

Validation:

- Missing snapshot means entitlement-gated operations are denied.
- Latest snapshot is authoritative for new entitlement-gated operations.
- Limits are non-negative.

## OperatorPrincipal

Separate administrative principal for platform operations.

Fields:

- `Subject`: stable operator or admin-key subject.
- `AuthenticationScheme`: operator authorization path.
- `DisplayName`: optional display name.

Validation:

- Operator principals do not create customer accounts or workspace memberships.
- Customer principals cannot perform operator-only operations unless separately authorized as operators.

## CustomerOwnedResource

Common ownership concept for records scoped to a workspace.

Fields:

- `Id`: resource identifier.
- `WorkspaceId`: owning workspace.
- `CreatedByAccountId`: optional customer account that created the resource.
- `UpdatedByAccountId`: optional latest customer account that changed the resource.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Examples:

- Workspace-owned package source.
- Saved runtime configuration.
- Runtime configuration version.
- Deployment target.
- Deployment run.
- Managed runtime environment.

Validation:

- Workspace membership is required before reading or mutating the resource.
- Role and entitlement checks apply before privileged mutations.

## State Transitions

First sign-in:

```text
trusted identity received -> external identity not found -> create account -> create personal workspace -> create owner membership -> return workspace context
```

Returning sign-in:

```text
trusted identity received -> external identity found -> update profile metadata and last seen -> return active workspace memberships
```

Workspace-scoped request:

```text
authenticated customer -> resolve account -> verify active workspace membership -> verify role -> verify entitlement if needed -> execute operation
```

Operator request:

```text
operator credential -> authorize operator policy -> execute operator-only operation without creating customer workspace membership
```
