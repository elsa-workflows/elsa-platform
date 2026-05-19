# Data Model: Managed Hosting Control Plane

## ManagedRuntimeEnvironment

Fields: `Id`, `WorkspaceId`, `ConfigurationVersionId`, `Region`, `Shape`, `Status`, `Url`, `CreatedAt`, `UpdatedAt`.

## RuntimeInstance

Fields: `EnvironmentId`, `Status`, `Health`, `StartedAt`, `StoppedAt`.

## ManagedInfrastructureResource

Fields: `EnvironmentId`, `Kind`, `ProviderResourceId`, `Status`.

## LifecycleEvent

Fields: `EnvironmentId`, `Timestamp`, `Action`, `Status`, `Message`.
