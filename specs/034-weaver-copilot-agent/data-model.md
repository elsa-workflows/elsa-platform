# Data Model: Weaver Copilot Agent

## WeaverSession

Represents one workspace-scoped assistant session.

Fields:

- `Id`: Valence Control session identifier.
- `WorkspaceId`: Workspace scope.
- `OrganizationId`: Organization/customer tenant scope when available.
- `AccountId`: Account that started the session.
- `CopilotSessionId`: SDK session identifier.
- `RoutePath`: Console route when the session started or was last updated.
- `ContextJson`: Safe current-page entity context such as application, environment, engine, revision, artifact, or package IDs.
- `Mode`: `Inspect`, `Plan`, or `Operate`.
- `ProviderMode`: `Disabled`, `GitHubCopilot`, `BringYourOwnKey`, or `Fake`.
- `Model`: Model identifier used for the session.
- `ReasoningEffort`: Optional reasoning effort.
- `Status`: `Active`, `WaitingForUser`, `WaitingForApproval`, `Executing`, `Completed`, `Failed`, `Canceled`, or `Archived`.
- `CreatedAt`, `UpdatedAt`, `CompletedAt`.

Relationships:

- Has many `WeaverMessage`.
- Has many `WeaverToolCall`.
- Has many `WeaverPlan`.

Validation:

- `WorkspaceId`, `AccountId`, `Mode`, and `Status` are required.
- `ContextJson` must be redacted and bounded.
- Provider API keys are never stored.

## WeaverMessage

Represents a visible or internal-safe assistant/user message.

Fields:

- `Id`
- `SessionId`
- `Role`: `User`, `Assistant`, `System`, or `Tool`
- `Content`
- `RedactionState`: `None`, `Redacted`, or `Omitted`
- `CreatedAt`
- `Sequence`

Validation:

- Content must be bounded.
- Raw secrets and provider keys must be redacted before persistence.

## WeaverToolCall

Represents one model-requested tool execution.

Fields:

- `Id`
- `SessionId`
- `ToolName`
- `ArgumentsJson`: Redacted argument summary.
- `ArgumentsHash`: Hash of original arguments when full arguments are unsafe.
- `ResultSummaryJson`: Redacted result summary.
- `AuthorizationResult`: `Allowed`, `Denied`, or `RequiresApproval`
- `Status`: `Started`, `Succeeded`, `Failed`, `Denied`, or `Canceled`
- `DurationMilliseconds`
- `TraceId`
- `CreatedAt`, `CompletedAt`

Validation:

- Tool calls must include workspace scope.
- Unsafe result payloads are summarized, not stored raw.

## WeaverPlan

Represents an immutable proposed operational action.

Fields:

- `Id`
- `SessionId`
- `Version`
- `PlanType`: `Deployment`, `Promotion`, `Rollback`, `RuntimeControl`, `EngineRegistration`, `SecretReference`, or `SetupGuidance`
- `Title`
- `Summary`
- `TargetJson`: Safe target entity references.
- `ImpactJson`: Safe expected impact.
- `ValidationJson`: Validations and blockers.
- `RollbackJson`: Rollback or remediation path.
- `Risk`: `Low`, `Medium`, `High`
- `Status`: `Draft`, `Blocked`, `ReadyForApproval`, `Approved`, `Rejected`, `Executing`, `Succeeded`, `Failed`, or `Canceled`
- `CreatedByAccountId`
- `CreatedAt`, `UpdatedAt`

Validation:

- Plan versions are immutable after approval.
- Mutating plans cannot execute unless `Status` is `Approved`.
- Targets must remain in the session workspace.

## WeaverPlanApproval

Represents the human decision on a plan.

Fields:

- `Id`
- `PlanId`
- `PlanVersion`
- `AccountId`
- `Decision`: `Approved` or `Rejected`
- `PermissionSnapshotJson`
- `ConfirmationId`
- `Reason`
- `CreatedAt`

Validation:

- Approver must have required permissions at decision time.
- Approval must reference the exact plan version.

## WeaverPlanExecution

Represents execution of an approved plan.

Fields:

- `Id`
- `PlanId`
- `PlanVersion`
- `Status`: `Queued`, `Running`, `Succeeded`, `Failed`, `Canceled`, or `Compensated`
- `LinkedResourceJson`: Deployment run, runtime command, confirmation, or audit IDs.
- `DiagnosticsJson`: Safe diagnostics only.
- `TraceId`
- `StartedAt`, `CompletedAt`

Validation:

- Execution is idempotent per approved plan version.
- Diagnostics must not include secrets or raw payloads.

## WeaverProviderConfiguration

Represents effective configuration, not persisted provider credentials.

Fields:

- `Enabled`
- `ProviderMode`
- `Model`
- `ReasoningEffort`
- `CredentialSource`
- `CopilotHome`
- `RuntimeConnection`
- `StreamingEnabled`
- `TelemetryEnabled`
- `MaxConcurrentSessions`
- `TurnTimeoutSeconds`
- `ToolResultMaxBytes`

Validation:

- BYOK provider credentials are resolved at session start/resume and never stored in Weaver records.
- Disabled scopes prevent session start.
