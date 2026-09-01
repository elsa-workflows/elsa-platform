# Elsa Commercial Platform / Elsa Cloud

## Vision

Build one coherent Elsa commercial platform around Elsa Control: Elsa Cloud, Elsa Control Cloud and Elsa Control Self-Hosted share the same core control plane. The Commercial Elsa Distribution and `elsa-specifications` supply governed runtime and feature metadata.

Elsa Cloud is a managed manifestation of Elsa Control, not a parallel orchestration architecture.

## Canonical context

- PRD: `docs/product/elsa-commercial-platform-prd.md`
- Current state: `docs/product/current-state-assessment.md`
- Target architecture: `docs/product/target-architecture.md`
- Roadmap: `docs/product/delivery-roadmap.md`
- Decision log: `docs/product/decisions.md`
- Operating model: `docs/product/program-operating-model.md`
- ADRs: `docs/adr/`

## Architecture summary

Elsa Control owns provider-neutral desired Elsa configuration. A resolved application plan is realized through Docker/output providers, Valence Azure stamp providers, or customer-side reconcilers. Version, topology, features, release channel and isolation are distinct. Existing artifact-driven desired state, remote commands, package metadata and console capabilities are reused.

## Milestones

- A — Architecture established
- B — Azure deployment proof
- C — Control-plane lifecycle proof
- D — Elsa Cloud walking skeleton
- E — Internal alpha
- F — Private beta
- G — Public Elsa Cloud
- H — Dedicated Elsa
- I — Elsa Private/custom code
- J — Elsa Control Cloud

## Critical path

Completed foundations: commercial image/version authority → resolved Elsa application
model → typed reconciliation → Azure provider/IaC and executable Milestone B proof →
durable Elsa Instance lifecycle/API → managed handoff/session foundation. Active P0
path: real local/Azure browser proof (#185) → capability-based multi-release/topology
runtime integration ([elsa-production-image#35](https://github.com/valence-works/elsa-production-image/issues/35)) → Elsa Cloud
walking-skeleton completion. #129 is an independent P1 recovery lane in review; it is
not evidence that production backup operations are already shipped.

## Tracking

Native GitHub sub-issues define the Program → Epic → Feature → Task/Spike hierarchy and provide roll-up progress. Issue bodies retain explicit `Part of`, `Blocked by`, and `Blocks` sections for readable context and cross-cutting dependencies. GitHub Project: https://github.com/orgs/valence-works/projects/7.

The Project uses the native GitHub issue types that are available to the organization (`Feature`, `Task`, and `Bug`) and a required `Work item type` Project field for the richer program taxonomy (`Program`, `Epic`, `Feature`, `Task`, `Bug`, `Spike`, and `ADR`). Program/Epic/Feature items use native `Feature`; Tasks, Spikes and ADR work use native `Task`; Bugs use native `Bug`. This preserves native issue semantics while making the full hierarchy queryable.

The six operational views are:

- **Execution:** board grouped by Status, excluding Done.
- **Roadmap:** roadmap grouped by Phase, using Start date and Target date.
- **Critical Path:** P0 work.
- **Agent Queue:** Agent Ready work whose Status is Ready.
- **Workstreams:** board grouped by Area.
- **Blocked:** work whose Status is Blocked.

## Epic tracking

- [x] #84 Architecture & Product Model
- [ ] #85 Versions, Distributions & Feature Catalog
- [x] #86 Deployment Providers & Azure Proof
- [ ] #87 Managed Instance Lifecycle & Deployment Stamps
- [ ] #88 Identity, Organizations & Onboarding
- [ ] #89 Subscriptions, Billing & Entitlements
- [ ] #90 Security, Isolation, Packages & Secrets
- [ ] #91 Operations, Observability, Backup & Upgrades
- [ ] #92 Elsa Control Web & Product Experience
- [ ] #93 Edge, Networking & Domains
- [ ] #94 Elsa Control Cloud, Self-Hosted & Portability
- [ ] #95 Launch Readiness & Documentation

## Program acceptance

The top-level Program tracking item remains open until all public product milestones explicitly accepted by Valence Works are complete or formally descoped.
