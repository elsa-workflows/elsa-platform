# Data Model: BYOC Deployment Targets

## DeploymentTarget

Fields: `Id`, `WorkspaceId`, `Name`, `Type`, `Status`, `CredentialReference`, `CreatedAt`, `UpdatedAt`.

## DeploymentPreview

Fields: `TargetId`, `ConfigurationId`, `Resources`, `Findings`, `GeneratedAt`.

## DeploymentRun

Fields: `Id`, `WorkspaceId`, `TargetId`, `ConfigurationVersionId`, `Status`, `StartedAt`, `CompletedAt`, `Findings`.

## DeploymentRunEvent

Fields: `RunId`, `Timestamp`, `Level`, `Message`, `Code`.
