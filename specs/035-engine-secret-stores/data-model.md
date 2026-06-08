# Data Model: Engine Credential Secret Stores

## Engine Credential Store

Workspace-owned configuration for a category of platform-to-engine credential references.

**Fields**
- Id
- WorkspaceId
- Name
- Type: LocalEncryptedDatabase, AzureKeyVault, KubernetesSecrets, EnvironmentVariableName, GenericExternalReference
- Description
- Status: Active or Archived
- Safe provider metadata
- CreatedAt/UpdatedAt
- CreatedByAccountId/UpdatedByAccountId
- ArchivedAt/ArchivedByAccountId

**Relationships**
- Has many engine credential references.

**Rules**
- Name is required and unique among active stores in a workspace.
- Store type is required and cannot be changed in a way that would invalidate existing references.
- Archived stores are not offered for new engine credential assignments.
- Store metadata must not include raw credential values or provider tokens.
- Store labels and descriptions must communicate that the store is for engine credentials only.

## Engine Credential Reference

Named credential entry under an engine credential store.

**Fields**
- Id
- WorkspaceId
- StoreId
- Name
- Locator
- Description
- Status: Active or Archived
- VerificationStatus: Verified, Missing, Expired, Unverified, NotVerifiable
- LastVerifiedAt
- LocalProtectedSecretPresent
- CreatedAt/UpdatedAt
- CreatedByAccountId/UpdatedByAccountId
- ArchivedAt/ArchivedByAccountId

**Relationships**
- Belongs to one engine credential store.
- May be assigned to many workflow engine registrations.

**Rules**
- Name is required and unique among active references in the same store.
- Local encrypted database references accept secret material during create/rotation and expose only whether protected material is present.
- External store references require a safe locator and do not accept raw credential values.
- Generic external references and environment variable names may remain NotVerifiable or Unverified.
- Archived references are not offered for new engine assignment.
- Existing engine assignments remain readable after archival.

## Workflow Engine Registration

Existing environment-scoped runtime endpoint registration.

**Fields affected by this feature**
- CredentialReferenceId
- CredentialProvider or store type display value
- CredentialReference or locator display value
- CredentialVerificationStatus
- CredentialLastVerifiedAt
- CredentialAssignmentStatus: Assigned or Deferred

**Relationships**
- Belongs to one deployment environment.
- May reference one active or archived engine credential reference.

**Rules**
- Engine registration may be created without a credential reference.
- Deferred credentials are explicit and block or mark unavailable credentialed platform-to-engine actions.
- Active credential references can be assigned later.
- Legacy provider/reference strings remain readable during transition.

## Credential Usage

Read model that shows where a credential reference is used.

**Fields**
- CredentialReferenceId
- EngineId
- EngineName
- ApplicationId
- ApplicationName
- EnvironmentId
- EnvironmentName

**Rules**
- Usage is shown before lifecycle actions that may affect existing engine communication.
- Usage must not expose credential material.
