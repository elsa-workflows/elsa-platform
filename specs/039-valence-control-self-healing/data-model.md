# Data Model: Valence Control Self-Healing

All authoritative entities are workspace-scoped unless explicitly platform-global. IDs are immutable GUIDs. External provider numeric/string IDs are stored separately from Valence Control IDs. Every mutable aggregate uses an optimistic concurrency token and normalizes timestamps to UTC.

## 1. HealingConfiguration

Controls adoption and safety for one application.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID | Primary key |
| `WorkspaceId` | GUID | Required; isolation boundary |
| `ApplicationId` | GUID | Required; unique with workspace |
| `DiscoveryEnabled` | bool | May be enabled independently of repair |
| `RepairEnabled` | bool | Requires discovery or explicit reports |
| `AutomaticMergeEnabled` | bool | Default false |
| `SignalProfileVersion` | string | Supported version required |
| `DefaultAttemptLimit` | int | Range 0–2; default 2 |
| `VerificationWindow` | duration | Positive, bounded by platform policy |
| `TimeBudget`, `ConcurrencyBudget`, `InferenceBudget`, `RepositoryRunBudget` | values | Non-negative and bounded |
| `ApplicationKillSwitch` | bool | Immediately blocks new dispatch/publication/merge |
| `CreatedAt`, `UpdatedAt` | timestamp | UTC |
| `Version` | concurrency token | Required |

Related `HealingEnvironmentConfiguration` rows override discovery, repair, thresholds, and kill-switch state for an application environment without weakening platform maximums.

`HealingWorkspaceConfiguration` is unique by `WorkspaceId` and holds the workspace emergency kill switch, timestamps, and optimistic concurrency token. Every discovery, repair-dispatch, publication, and automatic-merge gate resolves this row explicitly; absence is not treated as permission to bypass the workspace stop.

## 2. HealingSignalInboxItem

Durable post-redaction handoff from telemetry or the explicit incident API.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID | Primary key |
| `WorkspaceId`, `ApplicationId`, `EnvironmentId` | GUID | Required and mutually scoped |
| `IdempotencyKey` | string | Unique per workspace/application |
| `Source` | enum | `OpenTelemetry`, `ExplicitIncident` |
| `ProfileVersion` | string | Required |
| `OccurredAt`, `AcceptedAt` | timestamp | UTC |
| `RedactedEnvelopeJson` | JSON | Size-capped; never contains raw secrets |
| `EnvelopeHash` | SHA-256 | Integrity/audit |
| `Status` | enum | `Pending`, `Leased`, `Completed`, `Rejected`, `DeadLettered` |
| `LeaseOwner`, `LeaseToken`, `LeaseExpiresAt` | nullable | Set atomically when leased |
| `AttemptCount`, `NextAttemptAt` | values | Bounded retry metadata |
| `OutcomeCode`, `SafeOutcomeDetail` | nullable | Safe diagnostics only |

Database invariants:

- Unique `(WorkspaceId, ApplicationId, IdempotencyKey)`.
- Lease updates require current lease token.
- Completed/rejected/dead-lettered rows cannot return to pending.

## 3. ComponentManifest

Immutable application revision inventory.

| Field | Type | Rules |
|---|---|---|
| `Id` | GUID | Primary key |
| `WorkspaceId`, `ApplicationId`, `RevisionId` | GUID | Required; one active digest per revision |
| `SchemaVersion` | string | Versioned contract |
| `SourceRevision` | string | Commit SHA or trusted revision identity |
| `BuildId` | string | Optional external build correlation |
| `ManifestDigest` | SHA-256 | Unique for application/revision |
| `CanonicalJson` | JSON | Immutable and size-capped |
| `TrustState` | enum | `Unverified`, `Verified`, `Rejected`, `Revoked` |
| `VerifiedBy`, `VerifiedAt`, `VerificationMethod` | nullable | Required for verified state |
| `CreatedAt` | timestamp | UTC |

### ComponentManifestEntry

| Field | Type | Rules |
|---|---|---|
| `Id`, `ManifestId` | GUID | Required |
| `ComponentKey` | string | Unique within manifest |
| `Kind` | enum | `Application`, `Package`, `Assembly` |
| `Name`, `Version` | string | Required as applicable |
| `PackageId`, `PackageVersion` | nullable | NuGet identity |
| `AssemblyName`, `AssemblyVersion`, `PublicKeyToken` | nullable | Managed assembly identity |
| `ContentHash` | SHA-256 | Required for repair eligibility |
| `RelativePath` | string | Canonical non-rooted build path |
| `RepositoryUrl`, `RepositoryCommit`, `SourceRoot` | nullable | Advisory metadata |
| `IsDirectDependency` | bool | Build graph fact |

`ComponentDependency` stores directed edges between entries. Cycles are allowed where the resolved build graph contains them; duplicate edges are not.

## 4. SourceOwnershipBinding

Workspace-approved repair authority.

| Field | Type | Rules |
|---|---|---|
| `Id`, `WorkspaceId`, `ApplicationId` | GUID | Required |
| `Name` | string | Unique active name per application |
| `SelectorKind` | enum | `Application`, `Package`, `Assembly`, `ComponentKey` |
| `SelectorPattern` | string | Exact or explicitly declared glob syntax |
| `Priority` | int | Does not resolve conflicting authorities automatically |
| `ProviderConnectionId` | GUID | Active authorized connection |
| `RepositoryProviderId` | string | Immutable provider repository identity |
| `RepositoryOwner`, `RepositoryName` | string | Display/routing metadata |
| `TargetBranch` | string | Required |
| `WorkflowIdentity`, `WorkflowRevision` | string | Approved immutable workflow identity |
| `PathPolicyId`, `EvidencePolicyId`, `MergePolicyId` | GUID | Required |
| `Status` | enum | `Draft`, `Active`, `Suspended`, `Revoked` |
| `ApprovedBy`, `ApprovedAt` | required when active | Workspace owner |
| `CreatedAt`, `UpdatedAt`, `Version` | values | Audit/concurrency |

Multiple matches that resolve to different repository/workflow/path/merge authorities produce an `Ambiguous` attribution and block repair regardless of priority.

## 5. IncidentOccurrence

One accepted qualifying failure.

| Field | Type | Rules |
|---|---|---|
| `Id`, `InboxItemId` | GUID | Inbox item unique |
| `WorkspaceId`, `ApplicationId`, `EnvironmentId`, `RevisionId` | GUID | Required |
| `OccurrenceKey` | string | Unique per application |
| `OccurredAt`, `AcceptedAt` | timestamp | UTC |
| `Classification` | enum | Curated failure class |
| `Severity` | enum | `Informational`, `Warning`, `Error`, `Fatal` |
| `ExceptionType`, `OperationName` | string | Normalized |
| `NormalizedStackJson` | JSON | Bounded frames only |
| `TraceId`, `SpanId` | nullable | Correlation, never identity alone |
| `RetryState` | enum | `None`, `Retrying`, `Exhausted` |
| `FingerprintVersion`, `Fingerprint` | string | Deterministic |
| `EvidenceTier` | enum | `DefaultRedacted`, `Elevated` |
| `EvidenceDigest` | SHA-256 | Safe bundle integrity |

Raw message/body/request inputs are not persisted here. Safe normalized fragments may be held in the evidence bundle.

## 6. ComponentAttribution

Records every candidate component and why it did or did not authorize repair.

| Field | Type | Rules |
|---|---|---|
| `Id`, `OccurrenceId`, `ComponentEntryId` | GUID | Required |
| `BindingId` | nullable GUID | Approved binding if matched |
| `Confidence` | decimal | 0–1, deterministic score |
| `Basis` | enum set | Stack frame, assembly, package, SourceLink, explicit component |
| `Resolution` | enum | `Selected`, `Candidate`, `Ambiguous`, `Unauthorized`, `Unmapped` |
| `ReasonCodesJson` | JSON | Stable safe codes |

Exactly one selected binding is required before automated repair.

## 7. HealingIncident

Canonical problem aggregate.

| Field | Type | Rules |
|---|---|---|
| `Id`, `WorkspaceId`, `ApplicationId` | GUID | Required |
| `FingerprintVersion`, `Fingerprint` | string | Required |
| `RepairRepositoryKey` | string | Provider + immutable repository ID, or `observation-only` |
| `Status` | enum | See transition table |
| `Severity`, `Classification` | enum | Current aggregate values |
| `SelectedBindingId`, `SelectedComponentEntryId` | nullable GUID | Required for repairable state |
| `FirstSeenAt`, `LastSeenAt` | timestamp | UTC |
| `OccurrenceCount` | long | Monotonic |
| `ActiveEpisodeId` | GUID | Required while active |
| `WorkItemProjectionId` | nullable GUID | At most one active |
| `NeedsHumanReason` | nullable enum | Set when blocked/attempt-limited |
| `Version` | concurrency token | Required |

Database invariant: at most one active incident for `(WorkspaceId, FingerprintVersion, Fingerprint, RepairRepositoryKey)`. Provider-specific filtered indexes are used where supported; transaction/serializable fallback is tested for both providers.

### Incident state transitions

```text
Observed → ThresholdPending → ReadyForRepair → Repairing → PullRequestOpen
   │              │                 │              │              │
   ├──────────────┴──────→ ObservationOnly        ├→ NeedsHuman  ├→ Merged
   └─────────────────────→ Suppressed              └→ Failed      └→ Verifying

Verifying → Healed
Verifying → FailedVerification → ReadyForRepair | NeedsHuman
Any active state → Superseded | Waived
Healed/Failed/Superseded/Waived → new linked regression episode (never destructive reopen)
```

## 8. IncidentEpisode and EnvironmentImpact

`IncidentEpisode` bounds one occurrence/repair/verification cycle and links an optional previous episode. It records opened/closed times, producing revisions, target revision, outcome, and regression reason.

`EnvironmentImpact` is unique per episode/environment and stores first/last seen, occurrence count, producing revisions, current deployed revision, repair verification status, waiver/supersession actor, and closure time.

## 9. RepairWorkItemProjection

Provider-hosted issue projection.

| Field | Type | Rules |
|---|---|---|
| `Id`, `IncidentId`, `EpisodeId` | GUID | Required |
| `ProviderConnectionId` | GUID | Required |
| `ProviderWorkItemId`, `Number`, `Url` | provider values | Set after creation |
| `MachineSummaryHash` | SHA-256 | Avoids no-op updates |
| `ProviderState` | enum | Observed provider state |
| `ProjectionStatus` | enum | `Pending`, `Current`, `Stale`, `Failed`, `Deleted` |
| `LastProjectedAt`, `LastObservedAt` | nullable timestamp | UTC |

One active projection exists per incident episode. Occurrences update the machine-owned summary rather than append comments.

## 10. RepairAttempt

One bounded repair execution.

| Field | Type | Rules |
|---|---|---|
| `Id`, `IncidentId`, `EpisodeId`, `BindingId` | GUID | Required |
| `AttemptNumber` | int | 1–2 by default; unique per episode/target revision |
| `ProducingRevision`, `TargetRevision` | string | Producing may be unknown; target required before dispatch |
| `Status` | enum | `Queued`, `Dispatched`, `Running`, `ResultReceived`, `Publishing`, `PullRequestOpen`, `Succeeded`, `Failed`, `Stopped`, `Expired` |
| `EvidenceBundleId` | GUID | Required |
| `RepairClassification` | enum | `Reproduced`, `InferredHighConfidence`, `InsufficientConfidence`, `RevisionUnverified` |
| `NonceHash` | SHA-256 | One-time workload exchange binding |
| `LeaseOwner`, `LeaseToken`, `LeaseExpiresAt` | nullable | Atomic execution lease |
| `BudgetJson`, `UsageJson` | JSON | Safe bounded measures |
| `StartedAt`, `CompletedAt` | nullable | UTC |
| `OutcomeCode`, `SafeOutcomeDetail` | nullable | No raw model/repo output |

## 11. EvidenceBundle and EvidenceAccessDecision

`EvidenceBundle` contains a canonical, bounded, redacted JSON document plus digest, tier, provenance, omissions, size, and expiration. It is immutable.

`EvidenceAccessDecision` records requester identity, requested tier/fields, authorization, purpose, decision, safe reasons, approver, and time. Elevated data is released as a new bundle rather than mutating the default bundle.

## 12. RepairResultEnvelope

Immutable upload from a repository workflow.

| Field | Type | Rules |
|---|---|---|
| `Id`, `AttemptId` | GUID | One accepted result per run attempt/idempotency key |
| `WorkflowRunId`, `WorkflowRunAttempt` | provider values | Required |
| `BaseRevision`, `TargetRevision` | string | Required |
| `Classification`, `Confidence` | values | Required |
| `UnifiedDiff` | text/blob | Strict byte cap; inert until publisher validation |
| `PatchDigest` | SHA-256 | Required |
| `ChangedPathsJson` | JSON | Advisory; publisher recomputes |
| `ReproductionJson`, `RegressionJson`, `ValidationJson`, `RiskJson` | JSON | Bounded structured evidence |
| `SubmittedAt` | timestamp | UTC |

## 13. RepairPullRequest

Tracks the trusted publisher's provider mutation and observations: provider PR ID/number/URL, branch, base/head SHA, patch digest, draft state, classification, check snapshot, branch-protection snapshot, merge eligibility evaluation, merge state, merged SHA/time, and closure reason.

## 14. Policy definitions and evaluations

- `PathPolicy`: allowed roots, forbidden roots, file/line/byte maxima, binary/rename/symlink/submodule rules.
- `EvidencePolicy`: inference/reproduction requirements and fields/tier permitted.
- `MergePolicy`: human/automatic mode, required checks/verifier, forbidden change categories, rollback/stop requirement.
- `PolicyEvaluation`: immutable policy version/hash, input snapshot hash, complete gate results (`Pass`, `Block`, `Unknown`, `Stale`), decision, reasons, and time.

Policy edits create new versions. Active attempts keep their captured version; publication and merge re-evaluate kill switches and freshness.

## 15. ProviderConnection and ProviderOperation

`ProviderConnection` stores GitHub App installation/repository identity and protected credential references, never installation tokens.

`ProviderOperation` is the leased outbox for work-item projection, workflow dispatch, branch/PR publication, checks refresh, and merge request. It has a deterministic idempotency key, payload hash, attempt count, lease, next-attempt time, provider correlation ID, terminal outcome, and safe error.

## 16. WorkloadIdentityExchange

Short-lived record binding a verified GitHub OIDC token to one repair attempt: issuer, audience, subject, repository IDs, workflow ref/revision, source ref/SHA, run ID/attempt, actor ID, JWT ID, nonce hash, issued/expiry/exchanged times, capability token hash, and revocation state. JWT IDs and nonces are one-use.

### ManagedRepairInferenceReservation

Durable, one-per-attempt admission written before Valence Control invokes managed inference. It binds the request idempotency key and source-context digest to reserved inference units, an internal lease token/deadline, terminal outcome, and concurrency version. Proposal, attempt, reservation-completion, and audit writes commit in one transaction.

An expired `Leased` reservation is an indeterminate crash window: inference may have completed immediately before the process died. Because the managed provider contract does not offer a provider-side idempotency key, Valence Control never reacquires that reservation. It marks the reservation `Abandoned`, fails and releases the attempt, moves the active repairing incident to audited `NeedsHuman`, and preserves the inference budget against duplicate spend. A future provider adapter may opt into safe reacquisition only after supplying a proven durable idempotency contract.

## 17. ProviderWebhookDelivery and HumanCommand

`ProviderWebhookDelivery` records delivery ID, verified installation/repository/event/action, body digest, received/processed times, and safe outcome. The raw body is retained only for the minimum configured diagnostic window when policy permits.

`HumanCommand` stores the normalized retry/stop/evidence/waiver request, provider actor, linked Valence Control actor, provider permission snapshot, workspace permission decision, confirmation reference, status, and result. Provider text is never executed as instructions.

## 18. DeploymentObservation and VerificationResult

`DeploymentObservation` is unique by source/idempotency key and records workspace/application/environment/revision/deployed time, source (`ControlDeployment`, `ExternalDelivery`), trust identity, and evidence digest.

`VerificationResult` is unique per episode/environment/repaired revision and records window start/end, relevant-operation success count/last success, recurrence count/last recurrence, outcome (`PendingDeployment`, `Deployed`, `DeployedUnverified`, `Healed`, `FailedVerification`, `Superseded`, `Waived`), supporting occurrence/observation IDs, and decision time.

## 19. HealingAuditEvent

Append-only safe event:

| Field | Type | Rules |
|---|---|---|
| `Id`, `WorkspaceId` | GUID | Required |
| `Sequence` | long | Monotonic within aggregate/correlation |
| `AggregateType`, `AggregateId` | values | Required |
| `EventType`, `ReasonCode` | stable strings | Versioned vocabulary |
| `ActorType`, `ActorId` | values | Human, Valence Control, GitHub, workflow, agent |
| `CorrelationId`, `CausationId` | GUID | Required as applicable |
| `PolicyVersion`, `InputHash`, `OutputHash` | nullable | Decision integrity |
| `SafeDetailJson` | JSON | Redacted and capped |
| `OccurredAt` | timestamp | UTC |

No application contract permits update or delete.

## Relationship summary

```text
Workspace/Application
  ├─ HealingConfiguration ─ Environment overrides
  ├─ ComponentManifest ─ ComponentManifestEntry ─ ComponentDependency
  ├─ SourceOwnershipBinding ─ ProviderConnection/Policies
  └─ HealingSignalInboxItem ─ IncidentOccurrence ─ ComponentAttribution
                                      │
                                      ▼
                                HealingIncident
                                      │
                         IncidentEpisode ─ EnvironmentImpact
                           │          │
                           │          └─ VerificationResult ─ DeploymentObservation
                           ├─ RepairWorkItemProjection
                           └─ RepairAttempt ─ EvidenceBundle
                                  │
                                  ├─ WorkloadIdentityExchange
                                  ├─ RepairResultEnvelope
                                  └─ RepairPullRequest ─ PolicyEvaluation

Every transition ──► HealingAuditEvent
Every provider mutation ──► ProviderOperation
Every provider callback ──► ProviderWebhookDelivery
```
