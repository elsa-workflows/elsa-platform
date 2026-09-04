# Elsa Commercial Platform Current-State Assessment

Date: 2026-09-04

## Executive Assessment

Elsa Control is a meaningful modular control plane, not a prototype shell. Since the
initial assessment it has gained a durable Elsa Instance lifecycle/API, a provider-
neutral Azure reconciliation boundary, an executable Azure 3.8 Combined provider
proof, strict admission of the producer's signed schema 2.0 release manifest and
managed handoff/session behavior. The accepted composed evidence covers local and
public-TLS Azure browser journeys, with workflow creation, publication and execution
proven through the isolated managed-runtime browser harness.

The walking skeleton is accepted. Runtime integration is selected through declared
capability and immutable compatibility metadata for an arbitrary number of supported
release lines and applicable topologies; 3.8 remains the only producer-published line
proven live so far. The production API still registers an unconfigured Azure runner by
default, so proof-host success is not being presented as a production stamp service.

## Capability Matrix

| Capability | Current evidence | Assessment |
|------------|------------------|------------|
| Package/feature metadata | NuGet source ingestion, manifests, generator contracts, approvals and compatibility services | strong reusable foundation |
| Runtime images/topologies | signed/scanned `runtime-server`, `runtime-studio`, `runtime-combined` images and schema 2.0 release evidence are published; admitted manifests now project into a durable Control-owned catalog | immutable producer/admission/catalog path proven for 3.8 Combined; additional release lines require producer evidence and compatible runtime integration |
| Runtime Builder | saved configurations, package planning, bundles and template outputs | reusable; must feed instance/provider flow deliberately |
| Desired state | immutable revisions, structured records, canonical hashes, typed reconciliation, instance plan projection and durable operations | provider-neutral lifecycle foundation implemented |
| Deployment execution | durable runs/commands, leases, outcomes, runtime pull/sync, webhook advisory path | strong for artifact delivery to registered runtimes |
| Azure Elsa workload provider | durable typed runner/process/secret boundaries, proof host and live 3.8 Combined deployment/health/workflow/cleanup evidence | executable vertical slice proven; production API composition, placement and stamp operations remain |
| Identity/tenancy | OIDC/JWT/cookie identity, organizations, workspaces, memberships and permission grants | substantial; SaaS signup/invitations/federation lifecycle incomplete |
| Entitlements | workspace/organization snapshots and limits used for feed/workspace policy | reusable primitive; subscription/billing lifecycle incomplete |
| Secrets | secret-store and credential-reference APIs/UI; protected local values | useful engine-credential capability, not yet complete app configuration/secrets product |
| Console | shared React shell with packages, builder, deployments, credentials, logs, restored artifact routes and managed-instance open/retry UX | composed walking-skeleton evidence accepts onboarding and Azure open, with workflow execution proven in the isolated managed-runtime harness; broader billing and operations work remains |
| Observability | health checks, OTel dependencies, engine verification and console logs | operational baseline, not customer/SRE product completeness |
| Backups/DR | restore-to-new consistency contract, ADR, fault-injection harness and accepted live Azure proof were delivered through #129 and PR #205 | recovery contract proven; no production backup API/SLA claim |
| Commercial images | signed immutable schema 2.0 release manifest, signature, SBOM, provenance and vulnerability evidence are admitted and persisted as safe catalog facts | arbitrary release-line cardinality and capability-driven integration are implemented; 3.8 is the producer release proven live |

## Current Runtime and Deployment Flow

```text
Studio or API producer
  -> artifact metadata and payload reference
  -> immutable desired-state revision
  -> validation/promotion
  -> deployment run and durable command
  -> registered runtime/provider polls and claims
  -> runtime-side applier verifies and applies
  -> safe outcome and health report
```

This remains aligned with the Elsa Control Cloud customer-agent hypothesis. A
proof-only Azure provider can now create and reconcile a disposable managed workload,
but the production API deliberately keeps the Azure runner unconfigured. Environment,
stamp, capacity and customer-routing composition are therefore still product work.

## Reusable Assets

- Provider-neutral deployment abstractions, artifact identity and diagnostics.
- Workspace deployment services, permission grants, immutable revisions and run history.
- Runtime command sync and outbound-pull trust pattern.
- NuGet/package manifest pipeline and compatibility evaluation.
- Runtime Builder planning and Docker Compose/CLI-style output foundations.
- Organization/workspace accounts, authentication modes and entitlement snapshots.
- EF Core SQLite/SQL Server persistence and migrations.
- Shared console with route guards, query layer and deployment/operator modules.
- Aspire local orchestration, current Azure host deployment and OpenTelemetry service defaults.
- Existing accepted ADRs for package consolidation, remote apply/typed reconciliation and product-owned identities.

## Architectural Gaps

1. Governed catalog ingestion, queries and capability-driven managed-instance runtime
   selection are durable. Additional producer-published release lines and applicable
   topologies still require their own immutable evidence and live compatibility proof;
   3.8 Combined is the current proven release.
2. Legacy API configuration may remain as a compatibility surface, but it is no
   longer authoritative release-selection data.
3. The Azure provider vertical slice is not yet configured as the production API's
   stamp/placement/capacity service.
4. No executable isolation profiles or published boundary guarantees exist beyond the
   completed threat model and proof-specific controls.
5. No subscription/billing lifecycle, trial/grace/retention policy or usage/cost
   attribution exists.
6. The backup/restore-to-new contract and proof are accepted; production backup
   operations, customer domains, release rings and traffic-transition flows remain
   incomplete.
7. Customer-facing observability and SRE control across organizations/stamps are
   incomplete.
8. Arbitrary package provenance/build/isolation is not implemented.
9. The console walking-skeleton is accepted from composed evidence: public-TLS Azure
   sign-in, provisioning and seamless open under #185, plus real workflow execution in
   the isolated managed-runtime harness under #249. Billing, invitations and broader
   operations UX remain incomplete.
10. Runtime handoff integration is capability-driven for arbitrary supported release
    lines and applicable Server/Combined topologies, but every new producer release
    still requires immutable admission and compatibility evidence before use.
11. Nuplane is not integrated in production code; current references are
    presentation/configuration hints rather than package reconciliation.

## Intent-versus-Reality Divergences

- Historical deployment strategy described an in-process/CLI apply engine; ADR-0004 and production code moved apply to remote runtime/provider consumers.
- README and specs describe broader managed hosting and runtime operations as direction;
  managed-instance lifecycle/open UX now exists, while the full operations and audit
  product remains incomplete.
- Runtime image evidence recognizes Server/Studio/Combined; Control strictly admits
  schema 2.0 artifacts and durably projects their lifecycle and compatibility facts.
  The remaining gap is consuming those facts through the managed-instance/provider
  journey across producer-published release lines, not catalog cardinality.
- Azure deployment now has a real provider/proof-host vertical slice; the production
  API still fails closed with an unconfigured runner rather than silently presenting
  proof composition as a managed stamp service.
- Nearly all Spec Kit documents remain marked `Draft` even when their code is present, reducing confidence in specification status as program truth.
- The artifact console route/build failure was repaired in #115. Remaining console
  evidence concerns the complete managed-instance onboarding and browser journey, not
  that obsolete module gap.

## Immediate Recommendation

Milestone D's managed browser-to-workflow journey is accepted through #185, #249 and
the capability-based multi-release integration delivered by
[elsa-production-image#35](https://github.com/valence-works/elsa-production-image/issues/35).
Begin the MVP by decomposing #120 into independently reviewable entitlement,
billing-provider, enforcement and retention slices. Keep #122 as the explicit public
launch gate, and do not infer a production backup service from #129's accepted
recovery contract and proof.

## Evidence

- `README.md`
- `src/Hosting/ElsaControl.Api/appsettings.json`
- `src/Hosting/ElsaControl.Api/Program.cs`
- `src/Hosting/ElsaControl.AppHost/AppHost.cs`
- `src/Hosting/ElsaControl.Console/src/app/routes.tsx`
- `docs/adr/0004-deployment-engine-typed-reconciliation-hybrid.md`
- `docs/adr/0013-elsa-instance-aggregate-boundary.md`
- `docs/product/elsa-instance-aggregate.md`
- `docs/deployment/azure-workload-proof.md`
- `docs/spikes/127-managed-elsa-identity-handoff.md`
- `specs/010-runtime-image-metadata-api/spec.md`
- `specs/015-managed-hosting-control-plane/spec.md`
- `specs/028-runtime-command-sync/spec.md`
- `specs/031-organization-tenancy/spec.md`
