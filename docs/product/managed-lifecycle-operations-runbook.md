# Managed Lifecycle Operations Runbook

**Scope:** managed Elsa instance health, lifecycle work, reconciliation, and
operator response. This is the operational baseline for [#222](https://github.com/valence-works/elsa-control/issues/222).

**Important:** the controlled fixtures and tests behind this runbook are not
production availability evidence. They do not establish a 99.9% SLO, an alert
delivery guarantee, or a provider-specific production support commitment.

## Operator contract

Start with the workspace-scoped, read-only health projection:

```text
GET /api/workspaces/{workspaceId}/instances/{instanceId}/health
```

Use an authenticated session with access to the exact workspace. The response
contains the evaluated status and stable diagnostic code, UTC evaluation and
reconciliation timestamps, safe operation/run state and attempt metadata, and
safe alert records (`code`, `severity`, and an opaque deduplication identity).
It does not perform a lifecycle mutation. The operation and audit projections
are available at:

```text
GET /api/workspaces/{workspaceId}/instances/{instanceId}/operations/{operationId}
GET /api/workspaces/{workspaceId}/instances/{instanceId}/audit
```

Lifecycle mutations use the existing state machine through:

```text
POST /api/workspaces/{workspaceId}/instances/{instanceId}/operations
```

They require the current strong `If-Match` version and a fresh
`Idempotency-Key`. A health result is an observation, not permission to create
a second operation while an existing operation or deployment run is still
blocking.

## Status and stable codes

The evaluator gives durable `RecoveryRequired` and explicit operation/run
failure precedence over provider inference. A healthy result is emitted only
when desired lifecycle is `Running`, observed lifecycle is `Ready`, health is
`Healthy`, and no active work remains.

### Result diagnostic codes

| Code | Meaning and operator interpretation |
|---|---|
| `managed.lifecycle.healthy` | Running/Ready/Healthy with no active work. |
| `managed.lifecycle.degraded` | Known but not healthy; inspect endpoint and active work projections. |
| `managed.lifecycle.failed` | Provider projection is failed or unreachable. |
| `managed.lifecycle.unknown` | No trustworthy runtime health is available (including a deleted/tombstoned projection). |
| `managed.lifecycle.provider-unknown` | The provider observation is unknown or ambiguous. |
| `managed.lifecycle.stale` | An active operation or run exceeded its configured deadline. |
| `managed.lifecycle.reconciliation-stale` | The unknown/ambiguous reconciliation timestamp exceeded its deadline. |
| `managed.lifecycle.recovery-required` | Durable recovery is required; this is the single recovery gate. |
| `managed.lifecycle.operation-failed` | Fallback code when an operation failed without a more specific safe code. |
| `managed.lifecycle.run-failed` | Fallback code when a deployment run failed without a more specific safe code. |
| `managed.lifecycle.work-active` | Work is active but not yet stale or failed; allow the existing reservation to finish. |

An operation, run, or provider may supply a more specific safe diagnostic code
(for example, `provider.apply.failed`). Such a code is still bounded value-free
data; it is not a substitute for the stable alert codes below.

### Alert codes

| Code | Severity | Meaning and operator interpretation |
|---|---|---|
| `managed.lifecycle.recovery-required` | Critical | Provider state or cleanup is uncertain; reconcile before any replay. |
| `managed.lifecycle.operation-failed` | Critical | A lifecycle operation reached `Failed`. |
| `managed.lifecycle.run-failed` | Critical | Its correlated deployment run reached `Failed`. |
| `managed.lifecycle.stale-work` | Warning | A blocking operation or active queued/running run has no recent progress. |
| `managed.lifecycle.reconciliation-stale` | Warning | Unknown/ambiguous provider reconciliation is older than the boundary. |
| `managed.lifecycle.reconciliation-unknown` | Warning | Provider observation is unknown or ambiguous but not yet stale. |
| `managed.lifecycle.unhealthy-endpoint` | Warning or Critical | Endpoint/lifecycle projection is degraded (Warning), unreachable, or failed (Critical). |
| `managed.lifecycle.retry-exhausted` | Critical | Attempt number is at or above the configured maximum for non-terminal work. |

Alert deduplication identities are deterministic SHA-256 values derived from
opaque workspace/instance/work identifiers and the alert code. They are safe
correlation keys, not diagnostic payloads. The same snapshot and code must not
produce a new alert identity merely because evaluation time changed.

## Telemetry signals

The provider-neutral activity source and meter are
`ElsaControl.ManagedLifecycle`. Worker spans use
`managed_lifecycle.worker`; reconciliation spans use
`managed_lifecycle.reconciliation`. Counters are:

- `managed_lifecycle.operations.completed` for completed operations;
- `managed_lifecycle.operations.errors` for errors crossing an operation boundary;
- `managed_lifecycle.operations.transitions` for operation-state transitions;
- `managed_lifecycle.operations.retries` for attempts after the first; and
- `managed_lifecycle.endpoint.health.evaluations` for reconciliation health outcomes.

`managed_lifecycle.operations.duration` is the operation-duration histogram in
milliseconds.

Durably completed reconciliation replays remain visible as spans with outcome
`already_completed`, but do not increment completion, duration, or endpoint
health measurements again.

Use the operation outcome, lifecycle/health state, operation state, and
diagnostic code tags to filter these signals. Metric diagnostics use an explicit
fixed-code allow-list and collapse anything else to `unknown`. The allowed tag
list and redaction rules are defined in
[Tenant and redaction rules](#tenant-and-redaction-rules).

## Deadline and retry boundaries

The current API composition constructs the evaluator with its implementation
defaults. The options are `ManagedLifecycleOperationalHealthOptions`; any host
override must preserve these boundaries and update this runbook and its
controlled-fixture evidence.

| Policy | Current boundary | Clock used |
|---|---:|---|
| `OperationDeadline` | 10 minutes | Latest of heartbeat, last progress, started, or accepted time for blocking operation work. |
| `RunDeadline` | 10 minutes | Latest of heartbeat, last progress, started, or queued time for queued/running deployment runs. |
| `ReconciliationDeadline` | 10 minutes | `ReconciledAt`, but only when provider observation, observed lifecycle, or health is unknown/ambiguous. |
| `MaxAttempts` | 3 attempts | A non-terminal operation/run at or above this attempt count emits `managed.lifecycle.retry-exhausted`. |

The deadline comparison is strict: elapsed time must be **greater than** the
configured boundary. Exactly 10 minutes is not stale; the first evaluation
after 10 minutes is. A recent heartbeat or progress timestamp resets the
operation/run staleness clock. A confirmed old healthy projection is not stale
reconciliation. `RecoveryRequired` remains the result even if a stale-work or
retry-exhausted alert is also emitted.

The lifecycle worker's `Deployment:ElsaInstanceLifecycle:PollInterval` (five
seconds by default) controls polling cadence only; it is not an operational
deadline.

## Response paths

For every alert, capture only the UTC timestamps, status/code, severity, opaque
operation/run IDs, attempt, deduplication identity, and trace correlation. Check
the operation and audit projections in the same workspace before acting.

### Failed operation or run

1. Confirm the operation/run is terminal `Failed` and inspect its stable code and
   audit history.
2. Determine whether the provider boundary was definitely rejected or remains
   uncertain. A failed local validation/rejection is not the same as an
   unknown remote apply.
3. If no remote mutation could have been accepted and the observed lifecycle is
   `Failed` or `Degraded`, use the existing `Retry` action after the instance is
   no longer blocked, with the current `If-Match` and a new idempotency key. If
   that precondition is not met, use the state-machine-appropriate `Reconcile`
   path instead. Re-check health afterward.
4. If remote acceptance is uncertain, do not retry directly. Preserve the
   existing operation and move through the `RecoveryRequired` path below.

### Stale work or stale reconciliation

`Stale` is detection, not authorization to cancel or duplicate work.

- For stale operation/run work, inspect worker ownership, lease/progress and
  correlated provider state. Do not issue `Retry`, `Start`, or another mutation
  while the existing reservation is blocking. If the durable operation becomes
  `RecoveryRequired`, use the explicit recovery path; otherwise allow the
  current worker/reconciler to complete or escalate the incident.
- For stale unknown reconciliation with no blocking operation, request the
  existing `Reconcile` action with the current version and a new idempotency
  key. The provider must establish a concrete state before the instance is
  treated as healthy or eligible for a new action.

### Recovery required

`RecoveryRequired` is the single durable recovery gate. Stop new lifecycle
mutations, retain the operation/run and its correlation, and reconcile provider
state before making another remote call. Use the existing `Recover` action only
after that reconciliation, with the current `If-Match` and a new recovery
idempotency key. `Recover` resumes the same operation and increments its attempt;
it does not create a competing operation or silently replay an uncertain apply.

When provider state cannot be proven, or when the source is not safe to continue,
escalate to the sealed recovery-point and restore-to-new contract in
[#129](https://github.com/valence-works/elsa-control/issues/129) and
[Managed Elsa Instance Recovery Contract](managed-instance-recovery.md).

### Unhealthy endpoint

For `managed.lifecycle.unhealthy-endpoint`, confirm whether the projection is
`Degraded`, unreachable, or failed and use the safe provider/runtime health
diagnostics available to the operator. Do not mark an instance Ready, healthy,
or cutover-eligible from a control-plane enqueue alone. Once the endpoint is
known healthy and no operation is blocking, use `Reconcile` to refresh the
provider-neutral projection. A controlled `Restart` is an explicit lifecycle
operation and must not be used to bypass an uncertain provider result.

### Retry exhaustion

`managed.lifecycle.retry-exhausted` is critical at attempt 3 (or the configured
maximum) for non-terminal work. Stop automatic replay, inspect the exact
operation/run and provider correlation, and classify the failure as deterministic
or uncertain. Deterministic failure may proceed through the normal bounded
`Retry` path after the reservation is released; uncertain failure requires
`RecoveryRequired`. If a known-good revision exists, follow the rollback path
below. If the source cannot be trusted or rollback cannot safely restore service,
use #129 restore-to-new escalation.

## Rollback and restore-to-new escalation

Rollback is a controlled deployment action against a known-good successful
revision. It requires the existing deployment validation, explicit confirmation,
one active-run reservation, and the current workspace authorization boundary.
Use the existing deployment/run history and rollback flow described by the
[deployment UX specification](../../specs/022-deployment-ux/spec.md) and
[Elsa Instance aggregate contract](elsa-instance-aggregate.md). Rollback must
not be used to guess the outcome of an uncertain provider mutation; reconcile
that operation first.

Escalate to #129 when the source remains unhealthy or uncertain after bounded
reconciliation, rollback is unavailable or unsafe, or repeated retries have
exhausted. The restore-to-new response is:

1. Preserve the source and select one sealed, unexpired recovery point. For the
   initial objective, reject a point older than 24 hours.
2. Create a distinct isolated target and restore the relational state. Never
   reuse the source instance identity, database, endpoint, or placement.
3. Rebind the required secret references at the provider boundary; never copy or
   expose secret values.
4. Validate immutable inputs, target health, and restored workflow behavior
   before any cutover decision. The source remains authoritative and traffic is
   not mutated by the proof.
5. If the target operation or cleanup is uncertain, keep it recovery-owned and
   resume from the same immutable recovery checkpoint. Do not discover a newer
   point or delete the source to resolve uncertainty.

The recovery contract targets a 24-hour RPO and four-hour RTO for its initial
Dedicated profile, subject to its documented proof and limitations. It does not
promise multi-region DR, zero data loss, in-place rollback, or automatic
cutover.

## Tenant and redaction rules

- Organization is the customer tenant; Workspace remains the operational
  authorization and resource-isolation boundary. Every read and mutation must
  resolve the requested instance through the supplied workspace and deny or
  hide cross-workspace access. See [Organization tenancy](../../specs/031-organization-tenancy/spec.md).
- API responses and authorized operator records may contain safe enum state,
  UTC timestamps, attempt counts, opaque control-plane IDs, trace IDs, stable
  diagnostic codes, and deterministic deduplication identities.
- Metrics are low-cardinality. The managed lifecycle meter allows only
  `action`, `outcome`, `desired_lifecycle`, `observed_lifecycle`, `health`,
  `operation_state`, and `diagnostic_code`; never add organization, workspace,
  instance, operation, provider, endpoint, resource, name, or message labels.
- Logs/traces may correlate an authorized incident with opaque IDs where the
  access policy permits it, but must not include customer names, provider/Azure
  resource IDs, endpoint URLs or credentials, tokens, passwords, secret values,
  workflow/package/artifact payloads, request bodies, local paths, or raw
  exception/provider diagnostics.
- A diagnostic code is a bounded lower-case value-free token. Free-form failure
  text is not an operational code and must not be copied into alerts, metrics,
  API projections, or audit history.

## Controlled-fixture validation

Validate this runbook against controlled persistence/API fixtures for:

- Healthy, degraded, failed, unknown, stale, and recovery-required results;
- exact-deadline versus one-tick-past-deadline behavior;
- heartbeat/progress freshness and reconciliation freshness;
- retry exhaustion and stable alert deduplication across evaluation times;
- interrupted work with no duplicate provider mutation;
- cross-workspace read denial and redaction of unsafe diagnostics; and
- rollback selection and restore-to-new escalation without source mutation.

These checks prove deterministic contracts and safe response paths. They are not
production SLO measurements and do not establish Azure Monitor, PagerDuty, or
another vendor's alerting/delivery behavior.
