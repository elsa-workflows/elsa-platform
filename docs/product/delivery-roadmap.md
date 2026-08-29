# Elsa Commercial Platform Delivery Roadmap

## Milestones

| Milestone | Outcome | Exit demonstration |
|-----------|---------|--------------------|
| A — Architecture established | PRD, current-state map, domain/provider boundaries, ADR set, issue hierarchy and first Agent Ready tasks | architecture review and board audit |
| B — Azure deployment proof | select governed version/topology/features and obtain a working Elsa endpoint through an Azure provider | run basic workflow at returned URL |
| C — Control-plane lifecycle proof | durable Elsa Instance desired state converges through failure/retry | interrupt reconciliation and observe recovery |
| D — Elsa Cloud walking skeleton | sign in, create organization and instance, receive healthy Elsa and open it | browser-to-workflow E2E |
| E — Internal alpha | Valence dogfoods meaningful workloads | SLO dashboard, runbooks, upgrade and restore exercises |
| F — Private beta | selected customers run real workloads | security/support acceptance and feedback closure |
| G — Public Elsa Cloud | billing, backup, operations, security and support are launch-ready | signed launch checklist |
| H — Dedicated Elsa hardening | materially stronger guarantees beyond the public launch's dedicated runtime-and-database baseline, on the same control plane | isolation proof and cost model |
| I — Elsa Private | arbitrary packages in an approved isolated/provenance pipeline | malicious-package threat tests and rollback |
| J — Elsa Control Cloud | hosted control safely manages customer infrastructure | outbound-agent end-to-end proof |

## Phase Plan

### Phase 0 — Discovery and architecture

Deliver the canonical product docs, repository map, ADRs, risks, security workstream, GitHub program and executable critical path. No broad feature implementation.

### Phase 1 — Deployment proof

Critical sequence:

1. Audit commercial image/version/topology metadata and authoritative release source.
2. Define governed release/topology/feature contracts and resolved application plan.
3. Complete the typed analytical reconciliation core required by ADR-0004.
4. Spike Azure Container Apps/SQL/identity/routing and provider/IaC boundary.
5. Implement one Azure provider vertical slice.
6. Deploy, health-check and smoke-test a real Elsa configuration.

### Phase 2 — Walking skeleton

Add Elsa Instance lifecycle, placement, West Europe Dedicated provisioning, SaaS organization onboarding, progress UX, managed endpoint and the required seamless identity handoff into Elsa.

### Phase 3 — MVP

Harden provisioning/recovery; add entitlement/subscription lifecycle, backup/restore, health/observability, upgrade/rollback, domains, audit, SRE runbooks, incident handling and release automation.

### Later phases

Professional environment/promotion features (#131), Dedicated isolation hardening (#132), Private/custom-code delivery (#133), the Elsa Control Cloud customer-agent model (#103), and enterprise federation/governance (#134) remain deliberately coarse until the preceding milestone evidence is available.

## Schedule Baseline

These are planning targets, not unconditional delivery promises. Native dependency evidence and milestone exit criteria take precedence over dates. The GitHub Project Roadmap carries the same Start date and Target date fields for every current program item.

| Phase | Planned window |
|-------|----------------|
| Architecture | 2026-08-29 through 2026-09-18 |
| Deployment Proof | 2026-09-14 through 2026-10-30 |
| Walking Skeleton | 2026-11-02 through 2026-12-18 |
| MVP | 2027-01-04 through 2027-03-31 |
| Professional | 2027-04-01 through 2027-05-31 |
| Dedicated | 2027-06-01 through 2027-07-30 |
| Private | 2027-08-02 through 2027-09-30 |
| Elsa Control Cloud | 2027-10-01 through 2027-12-17 |
| Enterprise | 2028-01-03 through 2028-03-31 |

## Critical Path

```text
image/version authority
  -> resolved Elsa application model
  -> typed reconciliation boundary
  -> Azure provider/IaC spike
  -> Azure provider vertical slice
  -> managed endpoint health/workflow proof
  -> durable Elsa Instance lifecycle
  -> SaaS onboarding walking skeleton
```

Security threat modeling and commercial image provenance run in parallel but gate the deployment proof. Frontend onboarding design may run in parallel after the Elsa Instance contract is stable. Billing, broad observability and later isolation profiles do not block the first Azure proof.

## Earliest Agent Ready Tasks

The GitHub Project is authoritative for live IDs and status. The current Agent Ready queue is:

- Audit commercial image release metadata, version/tag/digest authority and Server/Studio/Combined composition.
- Complete ADR-0004's shared typed desired-state reader/validation-diff prerequisites.
- Spike Azure Container Apps, SQL, identity, edge and provider/IaC boundary with a disposable proof.
- Produce the threat model for customer packages and initial isolation profile.
- Define the deployment-proof executable test harness and evidence capture.
- Define the short-lived, signed one-time identity handoff into managed Elsa.
- Restore the missing Artifact Console module and frontend build health.

The identity handoff contract and threat model are captured in
[`docs/spikes/127-managed-elsa-identity-handoff.md`](../spikes/127-managed-elsa-identity-handoff.md).

These seven evidence/prerequisite or regression tasks are independently dispatchable. The provider-neutral resolved application plan remains blocked on the commercial image authority audit, and provider implementation waits for that contract plus the Azure spike and typed reconciliation prerequisite.

## Major Risks

| Risk | Mitigation |
|------|------------|
| Commercial image metadata/release ownership is not yet a stable machine-readable contract. | Complete #105 before resolving application/provider contracts; require immutable digest, topology and provenance metadata. |
| Shared isolation can dominate architecture and security. | Launch paid service with Dedicated only; require #110-derived executable evidence before adding Shared or Data-isolated. |
| Existing deployment terminology covers application artifacts/environments while the mission adds infrastructure-backed Elsa instances. | Preserve existing boundaries and introduce the Elsa Instance aggregate only after #114's migration/API analysis. |
| Azure provider work can leak infrastructure details upward or duplicate remote commands. | Enforce ADR-0007 and gate implementation on #106, #107 and #108. |
| Seamless identity into managed Elsa expands the walking-skeleton critical path. | Treat the signed one-time handoff as required and time-box the trust-flow spike before implementation. |
| Large deployment console/store files constrain safe parallel implementation. | Restore console build health in #115, then extract only stable capability seams as touched. |
| SRE, backup, security and cost can be postponed despite gating launch. | Treat Milestones E–G exit evidence as non-negotiable and keep those concerns as explicit Epics, not cleanup. |
