# ADR-0014: Managed Instance Backup and Restore-to-New

## Status

Accepted

## Date

2026-09-01

## Context

The initial Dedicated Elsa platform uses one Azure SQL database per managed
instance, immutable desired-state and resolved-plan records in Elsa Control, opaque
artifact references, provider-owned resource metadata, and external secret stores.
Those facts do not become application-consistent merely because each underlying
service has its own backup. A recovery point must bind the exact Control intent to a
quiesced Elsa relational point without copying secret values or provider resource
graphs into the customer model.

The product decision log sets initial recovery objectives of a 24-hour RPO and a
four-hour RTO, requires a backup before every upgrade, makes restore-to-new the first
recovery mode, and does not promise launch-time multi-region disaster recovery.
ADR-0012 separately governs durable artifact bytes and provenance.

## Decision drivers

- Never overwrite the only surviving instance while recovery is still being proven.
- Preserve exact desired-state, resolved release, artifact, and provider-plan facts.
- Keep raw secrets and provider credentials outside backup manifests and evidence.
- Fail closed when a relational point and its immutable Control metadata cannot be
  correlated.
- Measure recovery objectives through repeatable exercises rather than infer them
  from Azure configuration.
- Keep the contract provider-neutral above the recovery adapter.

## Options considered

### Option A — Independent service backups

Back up Elsa SQL, Control SQL, artifacts, and secret stores independently and select
the latest copy of each during recovery.

Benefits:

- Minimal orchestration.

Costs and risks:

- No cross-boundary consistency point.
- A restored database can reference a different desired revision, artifact, or
  provider operation than the one that produced its state.
- Independent secret restoration can copy values across a trust boundary.

### Option B — Sealed recovery-point manifest and restore-to-new

Quiesce the source, record a provider recovery point, and seal a safe manifest that
binds it to immutable Control facts. Restore the relational point into a new target,
rebind external secrets, validate immutable inputs, and health-check the target before
it becomes cutover-eligible.

Benefits:

- Explicit consistency and trust boundary.
- Source remains an available recovery reference while the target is validated.
- Provider adapters can evolve without changing customer recovery intent.

Costs and risks:

- Requires durable orchestration and a provider recovery adapter.
- Quiescence adds a bounded maintenance interval.
- Recovery exercises consume temporary provider capacity.

### Option C — In-place rollback

Restore the source database or resources in place.

Benefits:

- Fewer target resources.

Costs and risks:

- Destroys the strongest forensic and rollback reference.
- Makes partial restore and failed health validation materially harder to recover
  from.
- Azure SQL point-in-time restore creates a new database rather than overwriting the
  source.

## Decision

Choose option B.

1. A recovery point is a sealed, append-only manifest. It contains only safe
   organization/workspace/instance identity, capture time, source lifecycle and
   quiescence evidence, immutable desired-state revision and hash, resolved-plan URI
   and digest, artifact references and digests, safe provider snapshot reference and
   digest, the names of secret-reference slots that must be rebound, and a canonical
   manifest digest. It contains no secret value, connection string, token, workflow
   payload, local path, or provider credential.
2. The source must be quiesced and have no active or uncertain instance operation or
   deployment run before the relational recovery point is sealed. A timeout or an
   uncorrelated provider result cannot create a valid recovery point.
3. Restore always creates a new instance identity and new database. The source is not
   mutated or deleted by the restore workflow. The target begins isolated from
   customer traffic.
4. External secret values are not backed up by Elsa Control. Every required secret
   slot is rebound through a current provider-owned reference before target startup.
   Missing, extra, archived, or cross-workspace references fail closed.
5. The target becomes cutover-eligible only after the restored relational watermark,
   desired revision, resolved-plan digest, artifact digests, provider-plan identity,
   secret rebind set, HTTPS health, and a restored workflow read/execution probe all
   pass. Eligibility is evidence, not an automatic traffic mutation.
6. Standard and pre-upgrade recovery points remain restorable for 35 days. This is the
   initial engineering retention floor and matches the maximum Azure SQL short-term
   PITR window for non-Basic databases. A product or legal hold can extend retention;
   it never causes secret values to enter the manifest. Failed or incomplete target
   resources are cleaned up after their evidence is retained. Deleting a source
   instance does not purge held recovery points, while deleting the logical SQL server
   is prohibited until its required recovery points have expired or moved to an
   accepted longer-lived mechanism.
7. Create at least one recovery point in every rolling 24-hour interval for an active
   production instance and immediately before an upgrade or irreversible maintenance
   action. Measure RPO as incident/cutoff time minus the selected sealed point and RTO
   as recovery acceptance through target cutover eligibility. A run exceeding 24
   hours RPO or four hours RTO fails its objective; it does not change the target to
   Ready.
8. Initial Azure recovery uses Azure SQL point-in-time restore to a new database on
   the existing logical server. This proves local recovery, not regional disaster
   recovery. LTR, geo-restore, and multi-region failover remain separate evidence
   gates.

The detailed contract and proof procedure are in
[`managed-instance-recovery.md`](../product/managed-instance-recovery.md).

## Rationale

The sealed manifest creates one deterministic correlation boundary across systems
without pretending they share a distributed transaction. Restore-to-new preserves
the source and makes health-before-cutover enforceable. Reference-only secret
rebinding maintains the established trust boundary and avoids turning backup storage
into a second secret store.

Azure SQL currently supports point-in-time restore to a new database and configurable
short-term retention from 1 to 35 days for the selected General Purpose tier. Microsoft
documents that restore does not overwrite an existing database and does not restore
the source database's tags, so target ownership and retention tags must be asserted
again by the recovery adapter:

- <https://learn.microsoft.com/azure/azure-sql/database/automated-backups-overview>
- <https://learn.microsoft.com/azure/azure-sql/database/recovery-using-backups>
- <https://learn.microsoft.com/cli/azure/sql/db#az-sql-db-restore>

## Consequences

### Positive

- Recovery is an auditable lifecycle with exact, immutable evidence.
- Provider-specific backup locators stay below the instance/customer contract.
- A failed restore cannot silently replace a healthy source.
- The same upper contract can later use another database, object store, or customer
  provider adapter.

### Negative and accepted trade-offs

- The initial proof is same-region and same-logical-server.
- Quiescence may temporarily pause workflow execution.
- Thirty-five days of recoverability has storage cost and requires artifact/provider
  metadata retention alignment.
- Production scheduling, customer UX, legal-hold administration, LTR, and regional DR
  require follow-up delivery.

## Validation and evidence

Acceptance requires both deterministic fault-injection tests of the provider-neutral
recovery harness and a live disposable Azure exercise that:

1. creates and executes a workflow on the source;
2. seals a recovery point while the source is quiesced;
3. injects a distinguishable post-point change;
4. restores Azure SQL to a new database;
5. creates a new target identity and rebinds external secret references;
6. proves the pre-point workflow exists and executes on the healthy target while the
   post-point change does not;
7. records measured RPO/RTO and target cutover eligibility; and
8. preserves the source until target cleanup and evidence retention are confirmed.

Unit tests, compilation, or a successful database restore alone are insufficient.

## Revisit conditions

- A provider cannot meet the quiescence/correlation contract.
- Customer evidence requires a shorter recovery objective.
- Data residency, compliance, or cost requires LTR or cross-region copies.
- Elsa gains a certified online/application-consistent backup protocol that can
  safely replace bounded quiescence.
