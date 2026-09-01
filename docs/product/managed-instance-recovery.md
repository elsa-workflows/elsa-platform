# Managed Elsa Instance Recovery Contract

**Status:** Accepted architecture; executable proof in progress under
[#129](https://github.com/valence-works/elsa-control/issues/129)
**Decision:** [ADR-0014](../adr/0014-managed-instance-backup-and-restore.md)
**Initial objectives:** 24-hour RPO, four-hour RTO, same-region restore-to-new

## Promised outcome

An operator can select a sealed recovery point for one managed Elsa instance, create
a distinct isolated target, restore the instance's relational state, rebind its
external secrets, and prove the target healthy before any cutover decision. The
source remains unchanged and available throughout the exercise.

This contract does not promise multi-region disaster recovery, zero data loss,
in-place rollback, or migration of secret values.

## Recovery boundary

| Boundary | Recovery treatment | Excluded material |
|---|---|---|
| Elsa relational state | Provider recovery point selected at a recorded UTC cutoff after quiescence | Connection strings, database credentials, arbitrary SQL diagnostics |
| Instance intent | Exact immutable desired-state revision ID and canonical hash | Mutable request bodies or customer-entered reason text |
| Resolved application | Exact plan URI/schema/content digest and release-manifest evidence | Raw manifest, signature payload, provider resource graph |
| Artifacts | Immutable safe locator plus strict `sha256` digest; availability and digest are revalidated | Workflow/package/image payload copies in Control history |
| Provider realization | Opaque recovery snapshot and plan references with digests, retained below the provider boundary | Subscription, resource-group, server, identity, or credential details in customer evidence |
| Secrets | Ordered set of canonical secret-reference slots that must be rebound for the target | Raw values, protected ciphertext, tokens, generated passwords |
| Audit | Append-only recovery event types, actor/operation IDs, safe codes, timestamps, manifest digest, and measured objectives | Raw CLI output, request payloads, local paths, exception text |

The recovery point is not a second desired-state document. It binds immutable records
that already exist and records the provider recovery cutoff needed to reconstruct one
consistent target.

## Sealed recovery point

The provider-neutral envelope has this logical shape:

```text
RecoveryPoint
  Id, OrganizationId, WorkspaceId, SourceInstanceId
  CapturedAt, SourceQuiescedAt, SourceLifecycle
  DesiredStateRevisionId, DesiredStateHash
  ResolvedPlanReference, ResolvedPlanDigest
  Artifacts[] { Kind, Reference, Digest }
  ProviderSnapshotReference, ProviderSnapshotDigest
  RequiredSecretReferenceKeys[]
  ManifestDigest
```

All digests are lowercase `sha256` values. References are absolute, immutable, and
free of user information, query strings, fragments, control characters, and mutable
tags. Provider resource identifiers may be retained inside the provider adapter, but
the portable envelope and customer/audit evidence use only opaque safe references.

The canonical manifest digest covers the ordered normalized envelope. Sealing fails
when:

- the instance is not quiesced;
- any operation or deployment run is active or `RecoveryRequired`;
- the runtime/database cutoff cannot be correlated to the same quiescence interval;
- a desired revision, resolved plan, artifact, or provider snapshot is mutable,
  missing, or digest-invalid;
- a secret value rather than a reference key enters the envelope; or
- the source ownership path is incomplete or inconsistent.

## Create recovery point

1. Authorize the operation against organization, workspace, instance, entitlement,
   and the operator's backup permission.
2. Reserve one recovery operation and record a safe `RecoveryPointRequested` event.
3. Quiesce new workflow starts and wait for the configured drain boundary. Running
   instances that cannot reach the selected consistency policy fail closed; they are
   not snapshotted as if consistent.
4. Require no active/uncertain instance operation or deployment run.
5. Read the exact desired revision, resolved plan, artifact identities, secret-slot
   keys, and safe provider-plan identity.
6. Ask the provider for a relational recovery cutoff. The provider returns only an
   opaque snapshot reference, digest, and UTC time above its boundary.
7. Canonicalize and seal the recovery-point manifest, persist its digest and audit
   event, then resume the source when the surrounding maintenance operation permits.

A mandatory pre-upgrade point must seal before an upgrade, irreversible migration,
or destructive maintenance operation can pass its preflight gate.

## Restore to a new instance

1. Select one unexpired sealed recovery point and calculate its RPO age against the
   incident/cutoff time. Reject a point older than 24 hours for the initial objective.
2. Create a new target instance ID, operation, provider assignment, and database name.
   Reusing the source instance ID, database, endpoint, or placement assignment is an
   invariant violation.
3. Restore relational state into the new database. The source database and source
   instance remain untouched.
4. Project the sealed desired revision and resolved-plan identities to the target;
   revalidate every referenced artifact and digest.
5. Require an exact rebind for every secret-reference key. The adapter resolves
   values only at its narrow execution boundary and never returns them in a result or
   diagnostic.
6. Start the target with customer traffic disabled. Verify the restored relational
   watermark, exact pre-point workflow definition and execution behavior, expected
   absence of a post-point fault marker, HTTPS health, and safe provider facts.
7. Calculate RTO from accepted restore operation to completed target validation. More
   than four hours fails the objective.
8. Mark the target `CutoverEligible` only after every gate passes. A separate audited
   operation may later select routing/cutover. This proof does not mutate traffic.

## Partial failure and retry behavior

| Failure | Required result |
|---|---|
| Source cannot quiesce | No recovery point is sealed; source remains in its prior desired lifecycle |
| Provider snapshot is uncertain | Recovery operation becomes `RecoveryRequired`; no second snapshot is created until reconciliation |
| Manifest or immutable input mismatch | Restore is rejected before target start; evidence uses a stable value-free code |
| Relational restore fails or times out | Target remains isolated and cleanup/recovery-owned; source remains untouched |
| Secret rebind missing, extra, cross-workspace, or archived | Target does not start and cannot become cutover-eligible |
| Target health or restored-workflow probe fails | Target remains isolated; source remains authoritative; target is cleaned or retained for bounded investigation |
| Cleanup is uncertain | Recovery remains `RecoveryRequired`; source is never included in target cleanup scope |
| RPO or RTO objective is exceeded | Exercise result is Failed even if technical restoration eventually succeeds |

Retries use the same recovery-point identity and deterministic target scope. An
uncertain provider operation is reconciled before another mutation. A retry never
silently selects a newer backup, changes immutable inputs, or invents new secret
bindings.

## Retention, deletion, and legal hold

- Production and pre-upgrade recovery points have a 35-day initial restore window.
- At least one point must be available for every rolling 24-hour period while an
  instance is active. Azure SQL transaction-log frequency can provide a smaller
  technical gap, but the product objective remains 24 hours until the complete
  cross-boundary process is continuously evidenced.
- A legal/product hold blocks deletion of the sealed manifest, correlated provider
  recovery point, required immutable artifacts, and safe audit records. It does not
  copy or retain raw secret values.
- Expired points are deleted by a durable provider operation. Customer/audit history
  keeps only safe point identity, manifest digest, expiry, deletion outcome, and
  actor/time after provider absence is positively confirmed.
- A source instance deletion or a restored target cleanup cannot delete a held point.
  Deleting the logical SQL server is prohibited while required PITR/LTR evidence
  depends on it.
- Incomplete targets are not customer-visible instances and are removed after their
  bounded diagnostic/evidence window, subject to uncertain-operation reconciliation.

## Audit contract

Minimum append-only events:

- `RecoveryPointRequested`, `SourceQuiesced`, `RecoveryPointSealed`,
  `RecoveryPointFailed`;
- `RestoreRequested`, `RestoreTargetCreated`, `RelationalStateRestored`,
  `SecretReferencesRebound`, `ImmutableInputsVerified`;
- `RestoreHealthVerified`, `RestoreCutoverEligible`, `RestoreFailed`,
  `RestoreTargetCleaned`;
- `RecoveryPointHeld`, `RecoveryPointReleased`, `RecoveryPointDeleted`.

Events retain safe IDs, canonical manifest digest, operation/attempt, stable code,
timestamps, and measured RPO/RTO only. They exclude raw manifests, SQL/CLI output,
provider resource IDs, endpoint credentials, cookies, tokens, workflow payloads,
and secret values.

## Executable proof

The proof has two layers:

1. Deterministic harness tests inject failure at every stage and prove no failed run
   can become cutover-eligible or clean up the source.
2. A live disposable Azure exercise uses the accepted Elsa 3.8 Combined deployment
   proof boundary:
   - publish and execute a deterministic source workflow;
   - quiesce the source and record a UTC recovery cutoff;
   - inject a distinct post-cutoff workflow/change;
   - use Azure SQL PITR to create a new database;
   - provision a new identity, Key Vault binding, and isolated Container App target;
   - prove the pre-cutoff workflow exists and executes, the post-cutoff marker is
     absent, and the target is healthy before it is reported cutover-eligible;
   - measure RPO/RTO and retain only safe evidence; and
   - remove the target and disposable source only after exact ownership/absence
     checks pass.

The exercise uses General Purpose Azure SQL short-term retention configured to 35
days. Microsoft documents transaction-log backups at approximately ten-minute
intervals, but the exact interval is service-controlled. The proof requires the
selected time to follow the committed pre-point workflow and fall after Azure's
reported `earliestRestoreDate`; the successful PITR operation and restored workflow
state are the evidence that the point was usable. It never invents a “latest backup”
watermark that the live-database API does not expose.

## Acceptance evidence

#129 remains incomplete until all of the following are attached:

- exact code/commit and test run for the recovery harness;
- live Azure restore operation and target database identity as safe references;
- exact immutable Elsa image and resolved-plan/release-manifest digests;
- source and target workflow outcomes without payloads or credentials;
- measured recovery-point age and restore duration;
- proof that target health preceded cutover eligibility;
- source-preservation and target/source cleanup postconditions; and
- known limitations: same-region, no LTR/geo-restore, no automatic traffic cutover.
