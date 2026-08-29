# Elsa Commercial Platform — Product Requirements Document

Status: Canonical product direction

Owner: Valence Works

Last updated: 2026-08-29

## Product Vision

Valence Works will offer one coherent Elsa commercial platform built around Elsa Control. Customers configure and operate Elsa at the level of versions, topology, capabilities, environments, security and service outcomes; they do not need to become experts in the infrastructure that realizes those outcomes.

The product family is:

- **Elsa Workflows** — vendor-neutral open-source workflow runtime and application platform.
- **Commercial Elsa Distribution** — supported production images and artifacts for Elsa Server, Studio, Combined and future topologies.
- **elsa-specifications** — machine-readable package, feature and compatibility contracts.
- **Elsa Control** — the reusable application control plane for desired state, deployment, governance and operations.
- **Elsa Cloud** — Valence-operated Elsa Control plus Valence-operated Elsa workloads and infrastructure.
- **Elsa Control Cloud** — Valence-operated Elsa Control managing customer-owned infrastructure.
- **Elsa Control Self-Hosted** — the same control platform operated by the customer.

Elsa Cloud is a managed manifestation of Elsa Control, not a second orchestration system. Elsa Control Cloud and self-hosted Elsa Control reuse the same control-plane domain and provider contracts while differing in hosting, commercial and operational policy.

## Problem and Opportunity

Teams adopting Elsa can build workflow applications, but production operation still demands expertise across package compatibility, image selection, environment configuration, infrastructure, secrets, identity, upgrades, backups, networking, observability and incident response. This makes reliable Elsa estates expensive to create and difficult to govern consistently.

Valence Works can turn this complexity into a product by combining its Elsa expertise with a reusable control plane. The opportunity is larger than managed hosting: the same platform can serve fully managed customers, enterprises that retain infrastructure ownership, and customers that self-host the control plane.

## Product Principles

1. Customers buy managed Elsa outcomes, not Azure resource knowledge.
2. Elsa Control is an independently valuable product and the foundation of Elsa Cloud.
3. Desired Elsa configuration is provider-neutral; Azure is one realization target.
4. Elsa version, release channel, topology and feature set are separate dimensions.
5. Package and feature metadata flows from published package/specification data rather than a second hand-maintained SaaS catalog.
6. Arbitrary customer packages are arbitrary executable code and require a defensible isolation boundary.
7. Managed deployment uses durable desired state and reconciliation, not one-shot scripts.
8. Control-plane unavailability should not stop existing workflow execution where practical.
9. Operations, backup, upgrade, diagnosis and portability are product capabilities.
10. Thin end-to-end proofs outrank disconnected subsystem completeness.

## Personas

- **Elsa application builder** — configures an Elsa environment and builds workflows without managing infrastructure.
- **Platform operator** — manages dev/test/staging/production, packages, promotion, upgrades and health.
- **Organization administrator** — manages membership, roles, subscription and entitlements.
- **Security/compliance owner** — needs explicit isolation, audit, identity, secret and networking guarantees.
- **Enterprise infrastructure owner** — wants Valence-hosted control with workloads and data in customer infrastructure.
- **Valence cloud operator/SRE** — provisions, observes, updates and recovers the service across stamps and customers.
- **Self-hosted Elsa Control administrator** — operates the control plane without hidden Valence SaaS dependencies.

## Primary Customer Journeys

### Elsa Cloud onboarding

1. Visit `cloud.elsaworkflows.io` and sign up or sign in.
2. Create or join an organization.
3. Establish a trial or subscription entitlement.
4. Create an Elsa instance by selecting an available Elsa version, topology/profile, region, isolation level and feature preset.
5. Confirm the configuration and watch durable provisioning progress.
6. Receive a healthy managed URL.
7. Open Elsa without an unnecessary second authentication ceremony.
8. Create and run a basic workflow.

### Governed environment lifecycle

1. Create development, test, staging or production environments.
2. Submit immutable application artifacts and configuration declarations.
3. Preview content-aware promotion, compatibility and policy results.
4. Promote approved changes and observe deployment progress.
5. Validate health, inspect failures and roll back or restore safely.

### Elsa Control Cloud / customer-owned infrastructure

1. Create a hosted Elsa Control account and organization.
2. Register an approved customer-owned deployment environment.
3. Establish an outbound-authenticated trust relationship where required.
4. Configure Elsa version, topology and features.
5. Reconcile the workload locally and report safe health/state metadata.
6. Retain customer ownership of infrastructure, workload data and network policy.

### Self-hosted Elsa Control

1. Install and license Elsa Control in customer infrastructure.
2. Configure identity, persistence, secrets and deployment providers.
3. Use the same core configuration, deployment and operations capabilities without mandatory Valence control-plane connectivity.

## User Stories

1. As an Elsa user, I want an opinionated setup flow so that I can create a useful environment without understanding Azure.
2. As an organization administrator, I want users, roles and workspaces within an organization boundary so that access follows company ownership.
3. As a commercial operator, I want entitlements expressed as capabilities and limits so that product plans can evolve without plan-name conditionals.
4. As an application builder, I want to select any supported Elsa release line so that existing applications are not forced into an immediate major migration.
5. As a platform operator, I want major migration separated from patch/minor upgrades so that risk and validation are explicit.
6. As an application builder, I want to choose Combined or Server plus Studio independently of version so that topology matches my needs.
7. As a new customer, I want Standard, Minimal or Integration presets so that package-level complexity is optional.
8. As an advanced customer, I want compatible feature customization so that the runtime contains the capabilities my workflows require.
9. As a security owner, I want unsupported or incompatible features blocked before deployment so that unsafe configurations do not reach production.
10. As a customer using approved extensions, I want package provenance and compatibility visible so that I can assess supply-chain risk.
11. As a private-isolation customer, I want arbitrary packages deployed in a strong boundary so that my code cannot affect other customers.
12. As a customer, I want configuration declarations and secret references separated from secret values so that promotion remains safe.
13. As a platform operator, I want immutable desired-state revisions so that every deployment is reproducible and auditable.
14. As an operator, I want retries and reconciliation after interruption so that partial provisioning does not require manual reconstruction.
15. As an operator, I want health validation before traffic transition so that failed revisions do not become customer-visible.
16. As an operator, I want release channels and maintenance windows so that upgrades match my risk posture.
17. As an operator, I want a pre-upgrade backup and tested rollback path so that managed upgrades are recoverable.
18. As an organization administrator, I want environment-specific access grants so that production actions require appropriate permission.
19. As a customer, I want managed domains and TLS so that the service is usable without certificate administration.
20. As an enterprise customer, I want custom domains and private connectivity where entitled so that Elsa fits corporate network policy.
21. As a customer, I want instance and workflow health, failures, logs, metrics and traces scoped to my organization so that I can operate workflows.
22. As Valence support, I want audited, least-privilege support access so that incidents can be diagnosed without invisible customer access.
23. As a customer, I want backup retention and restore-to-new-instance so that accidental changes and incidents are recoverable.
24. As a billing owner, I want clear subscription state, limits and meaningful usage so that commercial operations are predictable.
25. As a customer with payment trouble, I want a grace/retention lifecycle so that one failed charge does not immediately destroy workloads.
26. As an Elsa Cloud customer, I want exportable application state and secret placeholders so that managed open source does not become captivity.
27. As a Valence operator, I want placement and capacity known per stamp so that failure and scale remain bounded.
28. As a Valence operator, I want release rings so that updates progress from internal to canary to stable evidence.
29. As an infrastructure-owning customer, I want outbound agent connectivity so that Valence does not need broad inbound network privilege.
30. As a self-hosted administrator, I want documented deployment providers and extension points so that the platform can fit my infrastructure.

## Functional Requirements

### Product and tenancy

- The control plane shall model organizations, memberships, roles, workspaces, subscriptions/entitlements and audit history as distinct concepts.
- An Elsa instance shall be customer-visible desired state; a deployment shall be its provider-specific realization.
- Elsa application-level tenants shall remain distinct from organizations, workspaces, subscriptions and Elsa Cloud instances.
- Self-hosted mode shall not require Valence billing or SaaS-only screens where they have no purpose.

### Version, topology and release policy

- Every instance shall identify an exact Elsa release line/version and a topology/distribution independently.
- The version catalog shall support any number of Elsa release lines across any number of major versions (for example 3.8, 3.9, 3.10, 4.0, 4.1 or 5.0); versions are data, not enum members or fixed major-version branches.
- Version lifecycle and availability policy shall be control-plane data, not frontend branches. Lifecycle states are `Preview`, `Supported`, `Maintenance` and `End of Support`.
- `Preview` requires explicit opt-in; `Supported` permits new and existing instances; `Maintenance` continues existing instances but blocks new ones; `End of Support` provides a defined migration window with explicit warnings.
- Every release line shall receive at least 12 months in `Maintenance`, followed by at least a 6-month `End of Support` migration-grace period. The release catalog may grant longer periods for an individual release line but shall not shorten these published floors.
- The platform shall not force or silently perform a major-version upgrade. Customers schedule major migrations explicitly.
- An instance shall pin a selected minor release line. After platform rollout-ring validation, patch releases within that line may be applied automatically with rollback protection; moving to another minor release line requires explicit customer approval.
- For standard non-emergency patch releases, the platform shall provide seven days' notice, apply the change during the customer's configured maintenance window and permit one deferral of up to fourteen days.
- Critical security or reliability patches shall provide 24 hours' notice and shall not be deferrable. An actively exploited vulnerability or imminent fleet-safety threat may be patched immediately, with customer notification as soon as practical. Failed post-patch health validation shall trigger automatic rollback.
- A customer-approved minor-line upgrade shall use a managed staged cutover: preflight compatibility checks, backup, target-revision provisioning, health validation, activity quiescence, required migration and traffic cutover. The platform shall promise rollback only when the governed catalog marks that exact transition rollback-compatible; an irreversible transition requires an explicit customer gate.
- A major-version change shall use a managed side-by-side migration of portable configuration, provider-backed secret references, workflow definitions and other catalog-certified artifacts. Persisted or running workflow instances are excluded by default; customers drain, complete or cancel them before cutover. State migration may be offered only when the exact source/target transition has a certified migration adapter.
- After a successful major-version cutover, the source instance shall be stopped and retained read-only for 30 days for audit and export, with no customer traffic or workflow execution. Customers may delete it earlier. Any executable rollback must occur before final cutover.
- The model shall distinguish patch/minor upgrade, major migration, release channel and internal rollout ring.
- The first deployment proof and initial Elsa Cloud release shall offer Elsa 3.8 only. Later versions may coexist when their commercial distribution, compatibility and support evidence is ready.

### Features and packages

- Feature availability shall derive from package/specification metadata and compatibility evaluation.
- Compatibility shall consider selected Elsa version, runtime kind/topology and package conflicts/dependencies.
- The default creation flow shall expose opinionated presets; advanced mode may expose compatible individual features.
- Extension policy shall distinguish built-in, Valence-approved and arbitrary customer packages.
- Arbitrary customer code shall not run on shared compute without a separately evidenced sandbox boundary.

### Desired state and deployment

- Elsa Control shall own versioned desired configuration independently of provider implementation.
- Deployment modes shall distinguish generated artifacts, direct execution and continuous reconciliation.
- Managed environments shall use durable, idempotent reconciliation with progress, retry, failure and history.
- A provider shall consume a provider-neutral resolved application plan and return safe deployment/health facts.
- Runtime/provider application shall not place Elsa Control in the workflow execution path.

### Isolation and placement

- The platform shall support an evolvable isolation spectrum: Shared, Data-isolated, Dedicated and Private, with highly isolated/sovereign deferred.
- The first paid Elsa Cloud release shall offer Dedicated isolation only: dedicated Elsa runtime and database per instance. Shared and Data-isolated profiles are deferred until their compute, data, secret and noisy-neighbor boundaries pass executable threat-model validation.
- Each profile shall publish exact compute, data, secret, network and extension boundaries.
- Customers select service/isolation outcomes; provider resource names remain internal.
- Placement shall evolve around stamps/cells that bound capacity, region, failure and isolation.
- The initial managed region shall be West Europe. The launch promise is EU/EEA residency for primary customer data and backups, with no customer-selectable region or multi-region disaster recovery until separately validated.

### Identity, access and routing

- The product shall support customer sign-up/sign-in, MFA/passkeys where supported, invitations, organization/workspace RBAC and service identities.
- Enterprise federation, SAML/OIDC and SCIM are staged capabilities, not launch prerequisites unless contracted.
- Elsa Cloud identity, organization authorization, instance authorization and Elsa runtime identity shall have explicit trust boundaries.
- The walking skeleton shall provide a short-lived, signed one-time identity handoff into managed Elsa so a signed-in customer does not encounter a second login ceremony.
- Managed domains, TLS and hostname routing shall hide unnecessary stamp identifiers and preserve custom-domain evolution.

### Operations

- The service shall expose lifecycle state, health, diagnostics, upgrades, backups/restores, audit and safe operational actions.
- Observability data shall maintain organization/instance/tenant isolation.
- The initial recoverable boundary shall include Elsa relational state, immutable desired-state/configuration and artifact references, plus provider metadata. Secret values remain in their owning secret provider and are rebound from references.
- Production instances shall receive automatic and mandatory pre-upgrade backups, restore-to-new-instance first, a 24-hour RPO target and a 4-hour RTO target. Multi-region disaster recovery is not a launch promise.
- Stripe Billing and Checkout shall own payment/subscription transactions while Elsa Control owns entitlement projection. Launch uses a 14-day trial, no execution-credit metering, a seven-day failed-payment grace period, then blocks new provisioning and upgrades while existing workloads remain running/readable. Final suspension starts a 30-day retention period with notices and export before deprovisioning/deletion.

## Isolation Profiles

| Profile | Initial product intent | Custom code |
|---------|------------------------|-------------|
| Shared | Deferred; shared runtime and data boundary discriminated by Elsa tenant | prohibited |
| Data-isolated | Deferred; shared or pooled compute with dedicated database/data boundary | prohibited |
| Dedicated | First paid profile; dedicated Elsa runtime and database per instance | Valence-approved extensions only at launch; arbitrary packages remain gated by Private/custom-code validation |
| Private | Strongly isolated compute, persistence, secrets and advanced networking | permitted through a validated immutable/provenance pipeline |
| Sovereign | Dedicated stamp/subscription and regulatory controls | future, demand-led |

Launch scope may begin with a smaller subset. Marketing names must not imply guarantees that implementation evidence does not support.

## Non-Functional Requirements

- **Availability:** the initial production Dedicated profile targets 99.9% monthly managed-runtime availability; Preview carries no availability commitment. Existing Elsa workloads continue through temporary control-plane unavailability where practical.
- **Durability:** desired state, deployment history and audit are durable; artifacts and backups have defined retention and integrity checks.
- **Security:** least privilege, managed identities, secret redaction, supply-chain verification, rate limiting, abuse prevention and audited support access.
- **Scalability:** add stamps and regions without redesigning instance desired state or provider contracts.
- **Recoverability:** every production profile has documented RPO/RTO, restore procedure and disaster-recovery validation; the initial target is 24-hour RPO and 4-hour RTO with restore-to-new-instance.
- **Operability:** alerts, runbooks, quota/capacity monitoring, cost attribution, certificate lifecycle and release rollback are launch work.
- **Portability:** customers can export workflows, version/topology/feature declarations, configuration, connection metadata and secret placeholders.
- **Accessibility and UX:** the primary flow is usable without infrastructure vocabulary and follows appropriate web accessibility standards.

## Security Expectations

- Maintain a dedicated threat model for organization authorization, tenant isolation, control/data-plane trust, agents, packages, secrets, backups and support access.
- Treat package/image/artifact provenance as a release gate.
- Store no raw secret values, provider tokens, workflow payloads or local paths in command/history records.
- Validate every isolation claim against deployable architecture and executable tests.
- Prefer outbound-authenticated customer agents; exact enrollment, authorization, rotation, revocation and update trust require an ADR and spike.

## Implementation Decisions

- Reuse Elsa Control's modular repository, domain services, artifact/desired-state model, runtime command channel, package catalog and React console.
- Preserve the remote-apply architecture established by ADR-0004; infrastructure providers may add reconcilers without reviving a misleading in-process runtime target abstraction.
- Use the existing console as the common frontend platform with deployment-mode capability composition; do not create a second unrelated Elsa Cloud frontend.
- Keep marketing at `elsaworkflows.io` separate from the authenticated product at `cloud.elsaworkflows.io`.
- Use Combined as the Elsa 3.8 deployment-proof and initial managed default while preserving Server plus Studio as a first-class topology.
- Treat checked-in Bicep/provider modules as production infrastructure source of truth; Aspire remains local orchestration and developer experience.
- Keep Nuplane runtime/image-side for the first proof. Elsa Control owns package intent, metadata and compatibility; direct Control-to-Nuplane orchestration is deferred to the Private/custom-package workstream.
- Validate Azure Container Apps, Azure SQL, Front Door, Key Vault, managed identity and stamps through bounded spikes and a deployment proof before accepting production ADRs.

## Testing Decisions and Milestone Seams

External behavior is the primary test boundary. Unit tests support domain rules, but milestone acceptance occurs at the highest useful seam:

- **Azure deployment proof:** from a selected version/topology/features through provider execution to a reachable, functioning Elsa endpoint.
- **Control-plane lifecycle proof:** desired state survives retries/interruption and converges to an observable deployment state.
- **Walking skeleton:** from unauthenticated sign-up through organization and instance creation to opening Elsa and running a basic workflow.
- **Upgrade proof:** backup, deploy new revision, health validation, traffic transition and rollback under injected failure.
- **Isolation proof:** executable tests demonstrate the claims of every offered profile.
- **Customer-agent proof:** enrollment, outbound command delivery, least privilege, revocation and offline recovery in customer infrastructure.

## Explicit Non-Goals for Initial Delivery

- AKS as a prerequisite, every Azure region, multi-cloud or every BYOC form.
- Azure Marketplace, sovereign deployments or complete enterprise SSO/SCIM.
- Arbitrary packages on shared compute.
- Complex execution-credit billing.
- Every networking topology, multi-region DR or advanced tuning UI.
- A second Elsa Cloud provisioning model or unrelated frontend.
- Moving Valence SaaS concerns into Elsa Workflows OSS unless generally useful upstream.

## Launch Stages and Success Criteria

| Stage | Demonstrable outcome | Exit evidence |
|-------|----------------------|---------------|
| Architecture established | Canonical PRD, boundaries, ADRs, risks and executable critical path | reviewed docs and Agent Ready first tasks |
| Azure deployment proof | Elsa Control realizes selected commercial Elsa configuration in Azure | reachable endpoint and functional workflow smoke |
| Control-plane lifecycle proof | durable instance desired state reconciles across retries | failure-injection and recovery evidence |
| Walking skeleton | user signs in, creates organization/instance and opens healthy Elsa | browser-to-runtime end-to-end test |
| Internal alpha | Valence runs meaningful workloads on the same product path | SLO, runbooks, upgrade/restore exercises |
| Private beta | selected customers operate real workloads | support, security and incident feedback closure |
| Public Elsa Cloud | billing, backup, operations, security and support are production-ready on the launch Dedicated runtime-and-database baseline | launch checklist and signed operational acceptance |
| Dedicated hardening/Private | expand the launch Dedicated baseline with materially stronger placement/isolation guarantees, then add isolated custom code without control-plane redesign | isolation and provenance validation |
| Elsa Control Cloud | hosted control plane safely manages customer infrastructure | customer-agent/BYOC end-to-end proof |

## Product Success Measures

- Median time from completed sign-up to healthy Elsa endpoint.
- Provisioning success and automatic recovery rate.
- Upgrade success, rollback and restore success rate.
- Availability/error-budget adherence by profile.
- Number of active organizations and meaningful workflow workloads.
- Operator effort and cost per instance/stamp.
- Security/isolation incidents and audit completeness.
- Percentage of managed configurations exportable and successfully restored elsewhere.

## Open Product Decisions

No Phase 0 product-owner decision remains open. The decision log records the selected launch policies. Azure Container Apps and the future customer deployment agent remain evidence-gated architecture hypotheses, while public launch still requires executable security, availability, recovery, support and legal acceptance evidence.

## Further Notes

This PRD owns product-level what and why. Architecture decisions live in `docs/adr/`; executable work and dependencies live in GitHub issues; program status and scheduling live in the Elsa Commercial Platform GitHub Project.
