# Data Model: Saved Runtime Configurations

## RuntimeConfiguration

Fields:

- `Id`
- `WorkspaceId`
- `Name`
- `Description`
- `IntentJson`
- `CreatedAt`, `UpdatedAt`, `SoftDeletedAt`

Validation:

- Workspace membership required.
- Name is required and unique per active workspace.
- Intent JSON must contain image, packages, sources, infrastructure, and local package options.

## RuntimeConfigurationVersion

Fields:

- `Id`
- `RuntimeConfigurationId`
- `VersionNumber`
- `Name`
- `IntentJson`
- `BundleLockHash`
- `CreatedAt`

Validation:

- Immutable after creation.
- Version number increments per configuration.

## BundleGenerationReference

Fields:

- `ConfigurationId`
- `VersionId`
- `GeneratedAt`
- `FindingCounts`
- `GeneratedFileCount`

Validation:

- Does not store file contents or secrets.
