# Elsa Commercial Platform Target Architecture

## System Context

```text
elsa-specifications + commercial image metadata
                     |
                     v
                 Elsa Control plane
 identity -> organization -> Elsa instance desired state -> placement/provider
                     |                              |
                     |                              +-> Valence Azure stamps (Elsa Cloud)
                     |                              +-> customer agent/target (Control Cloud)
                     |                              +-> local providers (self-hosted)
                     v
       audit, health, upgrades, backups, usage

Elsa data planes execute workflows independently of routine control-plane availability.
```

## Core Domain

| Concept | Responsibility |
|---------|----------------|
| Organization | customer/company ownership, membership and commercial boundary |
| Subscription | commercial lifecycle that grants entitlement snapshots; not a plan-name switch |
| Entitlement | capability/limit such as profiles, versions, instance count, custom packages or retention |
| Workspace | collaboration and authorization scope within an organization; existing Elsa Control boundary |
| Elsa Application Definition | reusable desired application composition: catalog-backed exact release line/version, topology, features, packages and configuration shape; the catalog supports an arbitrary number of major/minor lines |
| Elsa Instance | environment-specific desired managed state including region, isolation, capacity, domain, release/upgrade policy and current deployment |
| Elsa Tenant | Elsa runtime application tenancy; separate from organization/workspace/instance |
| Deployment Revision | immutable provider-resolved realization intent for an instance |
| Deployment | one provider-specific realization/revision and its operational state |
| Stamp | capacity, region, failure and isolation placement unit |
| Deployment Target/Agent | trusted execution boundary that reconciles local infrastructure/workload and reports safe facts |

Workspace should remain the authorization/container primitive already implemented. Elsa Instance is added as the customer-facing managed environment rather than renaming existing deployment applications/environments without a migration analysis.
The aggregate, lifecycle and migration contract is defined in [`elsa-instance-aggregate.md`](elsa-instance-aggregate.md) and is implemented additively against the existing deployment records.

## Desired-State Layers

```text
Customer intent
  version policy + topology + feature preset/overrides + package policy
  configuration shape + capacity + region + isolation + networking outcomes
        |
        v
Resolved Elsa application plan
  exact image digests + compatible package set + runtime composition
        |
        v
Provider plan
  stamp placement + provider resources + secret references + routing
        |
        v
Immutable deployment revision and reconciliation command
```

Provider-specific resource identifiers and Azure concepts start only in the provider plan. Customer intent remains portable.

## Deployment Provider Contract

A provider should:

1. declare supported capabilities and isolation/topology constraints;
2. validate a resolved application plan without mutation;
3. produce an explainable provider plan;
4. reconcile through idempotent commands and durable checkpoints;
5. report safe resource state, endpoint, health and diagnostics;
6. support cancellation/retry and eventually upgrade, rollback, restore and deprovision;
7. keep credentials and raw secret values outside command/history records.

Generated output, direct execution and continuous reconciliation are distinct provider modes. Docker Compose can generate artifacts; an Azure provider can reconcile managed resources; a customer agent can reconcile within a customer trust boundary.

## Accepted Initial Azure Realization and Deferred Hypotheses

- Direct Container Apps managed HTTPS ingress for the initial provider slice. Azure Front Door, custom domains, WAF policy and advanced edge routing remain deferred hypotheses.
- Azure Container Apps for initial Dedicated Elsa compute, accepted by ADR-0010 and the #108 deployment proof; later isolation profiles remain evidence-gated.
- Azure SQL with one database per Dedicated instance initially; pooled and later isolation-profile topologies remain evidence-gated.
- Key Vault and managed identities for platform-managed secrets.
- Log Analytics as the proven proof-time diagnostics foundation; production Azure Monitor/Application Insights/OpenTelemetry, tenant scoping, retention and alerting remain a separate observability acceptance gate.
- Artifact and backup storage remain provider/productization choices; backup/restore is a separate acceptance gate and was not accepted by the #108 proof.
- Checked-in Bicep driven by the provider; the initial West Europe Dedicated boundary is accepted, while multi-region, private-network, HA and broader stamp/cell behavior require separate evidence.
- AKS remains a future provider/profile where requirements prove it necessary.

## Frontend Architecture

Use one shared Elsa Control web platform with capability-driven shells:

- **SaaS shell:** organization onboarding, subscription, managed instances, Valence regions/profiles and SaaS operations.
- **Hosted-control shell:** organization and customer targets/agents without Valence workload billing where irrelevant.
- **Self-hosted shell:** local identity/licensing and customer providers, hiding Valence-only commercial operations.

Shared modules cover packages/features, application definitions, environments, artifacts, desired state, promotions, credentials, health and audit. Product-mode composition occurs at routes/navigation/capabilities, not scattered plan-name conditionals.

## Security Boundaries

- Identity provider to control-plane session and organization/workspace authorization.
- Control plane to Valence stamp provider identity.
- Control plane to customer agent through outbound-authenticated enrollment, scoped commands and revocation.
- Workload package/image/artifact supply chain.
- Organization/instance/tenant boundaries in data, telemetry, backups and support tooling.
- Provider secret resolution at the narrowest execution boundary.

## Evolution Sequence

1. Normalize governed version/topology/features and Elsa Instance desired state.
2. Prove one Combined (or owner-selected) Elsa workload in one Azure region/profile.
3. Add durable instance reconciliation and failure recovery.
4. Add SaaS onboarding and seamless open-Elsa flow.
5. Add operational readiness, subscriptions, backup, upgrade, domains and security evidence.
6. Add stronger isolation/custom code and later customer-agent reconciliation without changing the upper desired-state model.
