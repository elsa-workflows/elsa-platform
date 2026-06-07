# Data Model: Deployment Setup Domain Flow

## Deployment Environment

Existing application-scoped deployment lane.

**Fields used by this feature**
- Id
- WorkspaceId
- ApplicationId
- Name
- Tier/TierId
- CreatedAt/UpdatedAt

**Relationships**
- Belongs to one workflow application.
- Has zero or more workflow engine registrations.

**Rules**
- New environment creation does not create a workflow engine.
- Environment creation requires an active tier.

## Workflow Engine Registration

Existing environment-scoped runtime endpoint.

**Fields used by this feature**
- Id
- WorkspaceId
- EnvironmentId
- Name
- BaseUrl
- CredentialProvider
- CredentialReference
- CredentialReferenceId (optional)
- Capabilities
- Controls
- Health/verification metadata

**Relationships**
- Belongs to one deployment environment.
- May reference one credential-reference metadata record.

**Rules**
- Base URL must be absolute.
- New engine registration must use an active credential reference when one is selected from the registry.
- Legacy provider/reference strings remain readable when no registry reference exists.

## Secret Store

Workspace-owned metadata for an external secret provider/store.

**Fields**
- Id
- WorkspaceId
- Name
- Provider
- Description
- Status: Active or Archived
- CreatedAt/UpdatedAt
- CreatedByAccountId/UpdatedByAccountId

**Relationships**
- Has many credential references.

**Rules**
- Name is required and unique among active stores in a workspace.
- Provider is required.
- Archived stores are not selectable for new engine registrations.
- Raw secret values and provider tokens are never stored.

## Credential Reference

Workspace-owned metadata under a secret store that points to an externally managed credential.

**Fields**
- Id
- WorkspaceId
- SecretStoreId
- Name
- Reference
- Description
- Status: Active or Archived
- VerificationStatus: Unverified, Verified, Missing, Invalid
- LastVerifiedAt
- CreatedAt/UpdatedAt
- CreatedByAccountId/UpdatedByAccountId

**Relationships**
- Belongs to one secret store.
- May be used by many workflow engine registrations.

**Rules**
- Name is required and unique among active references in the same store.
- Reference is required and is safe metadata, not a secret value.
- Archived references are not selectable for new engine registrations.
- Existing engines can continue displaying archived or legacy references.
