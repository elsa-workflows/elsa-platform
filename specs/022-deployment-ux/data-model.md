# Data Model: Deployment UX

## WorkspacePermissionGrant

Server-authoritative permission assignment for deployment actions.

Fields:

- `Id`: grant identifier.
- `WorkspaceId`: owning workspace.
- `AccountId`: member receiving the grant.
- `Permission`: read deployments, manage deployment setup, manage desired state, preview promotion, execute deployment, execute rollback, execute runtime controls, or manage observability metadata.
- `GrantedByAccountId`: optional actor metadata.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `RevokedAt`: optional revocation timestamp.

Relationships:

- Belongs to a workspace and account.
- Used by deployment APIs and worker-side authorization snapshots.

Validation:

- Workspace membership is required before a grant is effective.
- Revoked grants do not authorize operations.
- Bootstrap grants may be created automatically for workspace owners, but frontend state is never authoritative.

## WorkflowApplication

Workspace-owned grouping for related deployment environments.

Fields:

- `Id`: application identifier.
- `WorkspaceId`: owning workspace.
- `Name`: display name.
- `Description`: optional description.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- `CreatedByAccountId`, `UpdatedByAccountId`: optional customer actor metadata.

Relationships:

- Has many `DeploymentEnvironment` records.
- Has many `DesiredStateRevision` records through environments.

Validation:

- Workspace membership and read permission are required for reads.
- Deployment setup permission and entitlement are required for mutations.
- Name is required and unique within a workspace.

## DeploymentEnvironment

Named deployment context within a workflow application.

Fields:

- `Id`: environment identifier.
- `WorkspaceId`: owning workspace.
- `ApplicationId`: parent workflow application.
- `Name`: display name such as Dev, Test, Stage, or Production.
- `Tier`: dev, test, stage, production, or custom.
- `DesiredRevisionId`: latest desired revision for the environment.
- `DeployedRevisionId`: latest successfully deployed revision, if any.
- `DeploymentStatus`: succeeded, running, blocked, failed, or rolled back.
- `DriftStatus`: in sync, drift detected, or unknown.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Has many `WorkflowEngineRegistration` records.
- Has many `DesiredStateRevision` records.
- Has many `DeploymentRun` records.
- Has many `ObservabilityBinding` records.
- Has many `DriftReportItem` records.

Validation:

- Environment belongs to the same workspace and application.
- Only one active deployment run may target an environment.

## WorkflowEngineRegistration

Concrete Elsa workflow engine attached to an environment.

Fields:

- `Id`: engine registration identifier.
- `WorkspaceId`: owning workspace.
- `EnvironmentId`: parent environment.
- `Name`: display name.
- `BaseUrl`: endpoint base URL.
- `Region`: optional region.
- `Version`: reported Elsa version.
- `CertificateStatus`: trusted, expiring, untrusted, or unknown.
- `CredentialProvider`: provider name.
- `CredentialReference`: provider-backed reference, never raw value.
- `CredentialVerificationStatus`: verified, missing, expired, or unverified.
- `LastHeartbeatAt`: latest observed heartbeat.
- `Health`: healthy, degraded, unreachable, or unknown.
- `HostingProvider`: optional hosting adapter name.
- `CreatedAt`, `UpdatedAt`: audit timestamps.

Relationships:

- Has many `EngineCapability` records.
- Has many `RuntimeControlDefinition` records.
- Has many `DeploymentRun` records as target engine.

Validation:

- Base URL must be valid and normalized.
- Credential values are never persisted in this table.
- Capabilities must be explicit; unsupported controls are rejected.

## EngineCapability

Advertised engine or hosting capability.

Fields:

- `Id`: stable capability identifier.
- `WorkspaceId`: owning workspace.
- `EngineId`: parent engine.
- `Label`: display label.
- `Boundary`: workflow, engine API, shell, or hosting.

Validation:

- Capability ID is unique per engine.
- Boundary must match a known runtime-control boundary.

## RuntimeControlDefinition

Control that can be executed only when its required capability is present.

Fields:

- `Id`: control identifier.
- `WorkspaceId`: owning workspace.
- `EngineId`: parent engine.
- `Label`: display label.
- `Boundary`: workflow, engine API, shell, or hosting.
- `RequiredCapabilityId`: capability required before execution.
- `Description`: short explanation for audit and UI.

Validation:

- Control cannot execute unless the required capability exists on the engine.
- Runtime control permission and explicit confirmation are required before execution.
- Direct API requests for unsupported controls fail closed.

## DesiredStateRevision

Immutable versioned deployment intent for an environment.

Fields:

- `Id`: revision identifier.
- `WorkspaceId`: owning workspace.
- `ApplicationId`: parent application.
- `EnvironmentId`: source or target environment.
- `RevisionNumber`: monotonic revision number within the environment.
- `Label`: display label.
- `Commit`: optional source commit or external revision reference.
- `ContentHash`: deterministic hash of structured desired resources.
- `AuthoredAt`, `CreatedAt`: timestamps.
- `CreatedByAccountId`: optional actor metadata.

Relationships:

- Has structured `DesiredWorkflowRecord`, `DesiredFeatureRecord`, `DesiredShellConfigurationRecord`, `DesiredRuntimeConfigurationRecord`, `DesiredSecretReferenceRecord`, `DesiredObservabilityBindingRecord`, and `DesiredEngineBindingRecord` children.
- Used by promotion comparisons and deployment runs.

Validation:

- Immutable after creation.
- Does not contain raw secrets.
- Secret values appear only as provider references.
- Manifest/artifact import and export are deferred from this slice.

## StructuredDesiredStateRecord

Common concept for desired-state child records.

Fields:

- `Id`: record identifier.
- `WorkspaceId`: owning workspace.
- `RevisionId`: parent desired-state revision.
- `Kind`: workflow, feature, shell configuration, runtime configuration, secret reference, observability binding, or engine binding.
- `Name`: stable resource name.
- `PayloadJson`: structured, safe payload for that resource kind.
- `ContentHash`: deterministic hash for diffing.

Validation:

- Payload cannot contain raw secrets.
- Kind-specific validators decide required fields.

## PromotionComparison

Stored or computed comparison between source and target environment revisions.

Fields:

- `Id`: comparison identifier when persisted.
- `WorkspaceId`: owning workspace.
- `SourceEnvironmentId`: source environment.
- `TargetEnvironmentId`: target environment.
- `SourceRevisionId`: source desired revision.
- `TargetRevisionId`: current target revision.
- `CreatedAt`: timestamp.

Relationships:

- Has many `DeploymentDiffItem` records.
- Has many `DeploymentValidation` records.

Validation:

- Comparison is read-only and cannot mutate target state.
- Promotion preview permission is required.

## DeploymentDiffItem

Categorized difference between two desired-state revisions.

Fields:

- `Id`: diff item identifier.
- `Category`: workflows, features, shell configuration, runtime configuration, secret references, observability, or engine bindings.
- `Name`: affected resource name.
- `SourceValue`: summarized source value.
- `TargetValue`: summarized target value.
- `Impact`: added, changed, removed, or unchanged.

Validation:

- Values must be safe summaries and must not contain raw secrets.

## DeploymentValidation

Validation result for preview, deployment, rollback, or control execution.

Fields:

- `Id`: validation identifier.
- `WorkspaceId`: owning workspace.
- `Severity`: pass, warning, or blocker.
- `Scope`: affected area.
- `Message`: user-facing message.
- `Code`: stable diagnostic code.
- `CreatedAt`: timestamp.

Validation:

- Blockers prevent deployment, rollback, and runtime controls.

## ActionConfirmation

Explicit confirmation for risky actions.

Fields:

- `Id`: confirmation identifier.
- `WorkspaceId`: owning workspace.
- `ActionType`: deploy, rollback, or runtime control.
- `TargetId`: target run, environment, engine, or control identifier.
- `ConfirmedByAccountId`: initiating user.
- `ConfirmedAt`: timestamp.
- `ExpiresAt`: timestamp after which the confirmation is invalid.
- `UsedAt`: timestamp once consumed.

Validation:

- Confirmation is required before deploy, rollback, or runtime control execution.
- Confirmation must be created by the same account that initiates the action.
- Confirmation is single-use and time-limited.

## DeploymentRun

Auditable queued deployment or rollback attempt.

Fields:

- `Id`: run identifier.
- `WorkspaceId`: owning workspace.
- `ApplicationId`: target application.
- `EnvironmentId`: target environment.
- `EngineId`: target engine.
- `SourceRevisionId`: desired revision being deployed.
- `PreviousDeployedRevisionId`: previous deployed revision, if any.
- `RollbackSourceRunId`: prior run used for rollback, if any.
- `Status`: queued, running, succeeded, failed, blocked, cancelled, rolled back, or recovery required.
- `ValidationOutcome`: passed, warnings, or blocked.
- `ConfirmationId`: confirmation consumed for the run.
- `ActorAccountId`: account that initiated the run.
- `QueuedAt`, `StartedAt`, `CompletedAt`, `CreatedAt`: timestamps.
- `WorkerId`: in-process worker instance that claimed the run, if running.
- `WorkerHeartbeatAt`: latest worker heartbeat while the run is claimed.
- `AttemptNumber`: durable processing attempt count.
- `RecoveryReason`: safe explanation when a stale claimed run is moved to `RecoveryRequired`.
- `FailureMessage`: safe error summary.

Relationships:

- Has many `DeploymentValidation` records.
- Produces deployment history events.

Validation:

- Deployment execution or rollback permission is required.
- Explicit confirmation is required.
- A target environment cannot have two active runs.
- Run history is append-only.
- Queued/running state is durable after process restart; queued runs remain processable and stale claimed runs move to `RecoveryRequired` without automatic replay.

## ObservabilityBinding

Persisted connection metadata for environment telemetry.

Fields:

- `Id`: binding identifier.
- `WorkspaceId`: owning workspace.
- `EnvironmentId`: target environment.
- `EngineId`: optional engine scope.
- `Kind`: logs, traces, metrics, or console stream.
- `Provider`: provider name.
- `Status`: connected, degraded, or unavailable.
- `Scope`: provider-specific safe scope summary.
- `CorrelatedRevisionId`: optional desired/deployed revision.
- `Sample`: safe status summary.

Validation:

- Provider credentials are referenced externally and not exposed.
- Live provider queries are deferred from this slice.

## DriftReportItem

Persisted metadata describing a known difference between desired and live engine state.

Fields:

- `Id`: drift item identifier.
- `WorkspaceId`: owning workspace.
- `EnvironmentId`: target environment.
- `EngineId`: target engine.
- `Area`: affected area.
- `Desired`: safe desired summary.
- `Observed`: safe observed summary.
- `Action`: review, redeploy, or import.
- `DetectedAt`: timestamp.

Validation:

- Drift reporting never silently changes desired or observed state.
- Live drift detection is deferred from this slice.

## State Transitions

Initial setup:

```text
workspace member -> permission check -> create workflow application -> create environment -> register engine -> cockpit shows durable setup
```

Desired-state revision:

```text
workspace member -> desired-state permission check -> create structured desired records -> compute revision hash -> immutable revision available for preview
```

Promotion preview:

```text
select source revision + target environment -> permission check -> compare structured desired records -> validate target -> show diff and blockers without mutation
```

Deployment run:

```text
member with deployment execution permission -> validate preview -> explicit confirmation -> enqueue durable run -> worker claims run -> apply -> update environment deployed revision -> append history
```

Worker recovery:

```text
process restart -> worker scans queued/stale running runs -> process queued runs -> mark stale claimed runs recovery required -> append recovery history
```

Rollback:

```text
member with rollback permission -> select prior successful run -> validate prior revision compatibility -> explicit confirmation -> enqueue rollback run -> update deployed revision -> append history
```

Runtime control:

```text
authorized user -> select engine control -> verify permission + entitlement + capability -> explicit confirmation -> execute adapter action -> append audit metadata
```
