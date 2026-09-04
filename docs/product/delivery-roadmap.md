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
completed: image/version authority
  -> resolved Elsa application model
  -> typed reconciliation boundary
  -> Azure provider/IaC spike and vertical slice
  -> managed endpoint health/workflow proof
  -> durable Elsa Instance lifecycle and API
  -> managed handoff/session foundation

completed: real local/Azure browser proof (#185)
  -> capability-based multi-release/topology runtime integration
     (elsa-production-image#35, https://github.com/valence-works/elsa-production-image/issues/35)
  -> managed workflow creation and execution (#249)
  -> Elsa Cloud walking-skeleton completion (#101)

active: decompose the MVP commercial lifecycle (#120)
  -> entitlement projection and enforcement
  -> Stripe webhook and subscription lifecycle
  -> suspension, retention, export and deletion evidence
  -> public-launch evidence gate (#122)
```

Milestones B, C and D now have executable evidence. The composed browser evidence
covers normal Entra/MFA over public TLS, the admitted immutable Elsa 3.8 Combined image,
seamless handoff, workflow publication and successful execution. Capability-driven runtime
integration remains cardinality-unbounded rather than selecting from a finite Elsa
version switch. The next delivery boundary is the MVP commercial lifecycle; public
launch still depends on billing, operations, security and support evidence rather than
the walking-skeleton proof alone.

## Execution Progress and Live Queue

The GitHub Project and native issue relationships are authoritative for live IDs,
status and dependency order. The initial prerequisite queue produced these outcomes:

- Completed the commercial image authority audit and producer manifest foundation (#105 and `elsa-production-image#27`).
- Completed ADR-0004's typed desired-state reconciliation prerequisite (#107).
- Completed and accepted the disposable Azure workload proof and cleanup hardening (#108, #146, #147 and #150).
- Completed the customer-package and isolation threat model (#110).
- Defined the short-lived, signed one-time identity handoff contract (#127).
- Restored the Artifact Console module and frontend build health (#115).
- Completed the provider-neutral Elsa Instance lifecycle and API slices (#114 and its
  implementation Tasks).
- Completed the durable Azure provider vertical slice and executable Milestone B proof
  (#125 and #126).
- Admitted the producer's signed schema 2.0 release manifest while retaining distinct
  immutable OCI subject and payload identities (#158 and #202).
- Passed the real local and Azure public-TLS managed-browser proof under #185 using the
  account owner's normal Entra/MFA interaction, including one-time handoff, replay,
  logout and expiry behavior.
- Generalized the producer/runtime integration through declared capabilities and
  immutable compatibility evidence in
  [elsa-production-image#35](https://github.com/valence-works/elsa-production-image/issues/35)
  and the corresponding Elsa Control admission work.
- Completed the browser-to-runtime workflow creation, publication and execution proof
  under #249, closing the Elsa Cloud walking-skeleton Feature #101.

The identity handoff contract and threat model are captured in
[`docs/spikes/127-managed-elsa-identity-handoff.md`](../spikes/127-managed-elsa-identity-handoff.md).

There is no remaining Agent Ready implementation leaf after the walking skeleton. The
next planned Feature is #120, whose decided commercial policy must be decomposed into
small dependency-ordered Tasks before dispatch. #214 remains deliberately blocked
until the required CI workflow can be protected from pull-request-controlled changes.

## Major Risks

| Risk | Mitigation |
|------|------------|
| A proof centered on Elsa 3.8 could hard-code a finite version or topology switch. | Preserve the capability-driven contract delivered by [elsa-production-image#35](https://github.com/valence-works/elsa-production-image/issues/35), #97 and #247: select immutable compatible artifacts from catalog data for arbitrary supported release lines. |
| Shared isolation can dominate architecture and security. | Launch paid service with Dedicated only; require #110-derived executable evidence before adding Shared or Data-isolated. |
| The proven Azure provider could be mistaken for production stamp composition. | Keep the API default fail closed while the production runner/stamp boundary is configured; retain provider IDs and reconciliation metadata behind ADR-0007. |
| Seamless identity can regress after the accepted walking-skeleton proof. | Retain #185 and #249 as release-gate browser coverage for public TLS, normal Entra/MFA, replay/expiry/logout protections and actual workflow execution. |
| Backup/restore design can be mistaken for a production recovery service. | Treat #129 as an accepted recovery-contract proof only; track production API, scheduling, retention and operator exercises as separate implementation slices. |
| SRE, backup, security and cost can be postponed despite gating launch. | Treat Milestones E–G exit evidence as non-negotiable and keep those concerns as explicit Epics, not cleanup. |
