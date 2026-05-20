# Data Model: Runtime Operations

## RuntimeLogEntry

Fields: `EnvironmentId`, `Timestamp`, `Level`, `Message`, `Redacted`.

## RuntimeMetricSample

Fields: `EnvironmentId`, `Timestamp`, `Name`, `Value`.

## BackupRecord

Fields: `EnvironmentId`, `Status`, `CreatedAt`, `CompletedAt`, `RestoreTestedAt`.

## UpgradePlan

Fields: `EnvironmentId`, `FromVersion`, `ToVersion`, `Status`, `RollbackVersion`.

## OperationalEvent

Fields: `EnvironmentId`, `Timestamp`, `Action`, `Actor`, `Result`.
