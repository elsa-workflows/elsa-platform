# ADR-0013: Introduce a Provider-Neutral Elsa Instance Aggregate

## Status

Accepted

## Date

2026-08-29

## Context

Elsa Control already persists workspace-owned deployment applications,
environments, runtime engine registrations, immutable desired-state revisions,
deployment runs and remote commands. The commercial product needs a customer-visible
managed Elsa environment with release policy, topology, features, placement
requirements and lifecycle operations. Reusing an existing deployment environment
as that identity would conflate an application deployment target with a managed
instance and would make provider-specific lifecycle details leak into customer intent.

The product also supports an arbitrary number of Elsa release lines. A first launch
example may use Elsa 3.8 Combined in a Dedicated profile, but the model must not
become an Elsa-major enum or a permanent one-container invariant.

## Decision

Introduce `ElsaInstance` as a new aggregate root under `Organization` and `Workspace`.

- The instance owns customer intent: release line/version policy, topology/features,
  safe configuration references, placement outcomes, desired lifecycle and safe
  current status.
- The instance persists a canonical identity binding for managed handoff. Its
  audience is the deterministic, instance-specific value
  `urn:elsa:instance:{lowercase-canonical(instance-id)}`; its callback is the exact
  normalized URI built from the verified customer-visible endpoint origin and fixed
  handoff path. The
  binding version changes atomically when the endpoint/domain rotates, and the
  handoff authorizer resolves audience, callback and version from this record at
  both issue and redemption.
- A managed instance maps explicitly to one existing deployment environment at first.
  Existing applications/environments/engines remain valid for generic artifact
  delivery and customer-owned targets; they are not renamed or silently inferred to be
  instances.
- `WorkspaceDesiredStateRevision` remains the immutable intent source and
  `ResolvedElsaApplicationPlan` remains the exact, provider-neutral resolved input.
  Deployment runs, commands, leases and runtime reports remain execution records
  below the aggregate boundary.
- Desired and observed lifecycle are separate. Mutations commit intent and enqueue a
  durable operation; only a health-gated provider/runtime report can project `Ready`.
- Release lines, topologies, features, profiles, capacity and regions are catalog or
  policy values. Adding `3.9`, `3.10`, `4.0`, `4.1`, `5.0`, or any later line requires
  catalog admission evidence, not a schema or code change.
- Placement intent contains only provider-neutral outcomes. Provider resource IDs,
  credentials, raw secrets and infrastructure topology stay in provider adapters.
- Mutations use optimistic concurrency and durable idempotency records. One active
  instance operation and one active or uncertain deployment run per mapped
  environment are permitted. The API commits intent/operation/outbox first; a worker
  later resolves asynchronously and atomically commits the resolved plan, operation
  claim, deployment-run insert and reservation. Filtered unique database indexes
  enforce the invariant; `Queued`, `Running`, and `RecoveryRequired` retain the run
  reservation. Uncertain work is reconciled/recovered before a retry.
- Major migrations use a durable side-by-side migration record. The source's exact
  release/version/digest is retained read-only/stopped for 30 days after cutover (or
  until an audited early release), rather than being overwritten by the target.
- The current instance projection contains a dereferenceable immutable plan URI and
  exact resolved release line/version/manifest/component digests.
- Instance lifecycle and audit persistence are additive to the current EF Core catalog,
  with SQLite and SQL Server migrations, an explicit nullable environment binding,
  a filtered unique one-to-one binding index on the globally unique instance ID and
  ownership-integrity enforcement.

The detailed aggregate shape, API, persistence migration, examples and implementation
sequence are in [`Elsa Instance Aggregate and Lifecycle Contract`](../product/elsa-instance-aggregate.md).

## Alternatives Considered

### Rename deployment environments as instances

Rejected. Existing environments are named contexts under a workflow application and
can contain registered engines and artifact-delivery history. A managed instance is a
customer-facing lifecycle/placement identity. A one-to-one binding preserves existing
customers and permits both concepts to evolve independently.

### Put provider resources directly on the instance

Rejected by ADR-0007. Provider resource identifiers, credentials and infrastructure
shapes would make customer intent non-portable and force every future provider to
recreate an Azure-shaped schema. Providers instead persist an opaque assignment and
report safe outcomes through the provider contract.

### Store one fixed Elsa major/version enum

Rejected. Release lines are catalog data with independent compatibility and support
lifecycles. A fixed enum would turn the first 3.8 launch into a schema migration for
every subsequent release line and invite major-specific frontend branches.

### Make the instance itself the runtime tenant

Rejected. An Elsa runtime tenant is an application-level identity/data-plane concern.
The instance may reference it for authorization and handoff, but Control's
organization/workspace authorization remains authoritative and runtime execution state
stays outside the control-plane aggregate.

## Consequences

- Managed instance APIs and UX gain a stable customer identity without breaking the
  existing deployment subsystem.
- The first lifecycle implementation must add an instance operation/outbox or durable
  queue seam and project provider reports; a synchronous in-process apply façade is
  not introduced.
- Delete is a durable cleanup intent. It can be requested from `Pending`,
  `Provisioning`, `Updating`, `Stopping` or `Unknown`, but cancellation/recovery and
  positive provider absence evidence are required before the retained tombstone is
  marked `Deleted`; an uncertain run remains reserved.
- Existing deployment reads can continue during migration, but managed clients must
  eventually use instance projections. Unbound environments remain supported for
  customer-owned/self-hosted targets.
- Backup/restore, upgrade, handoff, isolation, placement and console work can depend
  on explicit instance IDs and ownership without copying provider or runtime state.
- Additional tables and links require parallel SQLite/SQL Server migration coverage,
  authorization tests, idempotency/concurrency tests, and no-secret/no-provider-ID
  response assertions.
