# ADR-0010: Initial Azure Workload Platform and Stamp Topology

## Status

Proposed

## Date

2026-08-29

## Context

Current Azure assets deploy the Elsa Control host to Azure App Service and Azure SQL. They do not provision Elsa workloads. The first deployment proof needs container compute, relational persistence, managed identity/secrets, routing/TLS, telemetry and durable provider reconciliation while preserving future dedicated/private and regional evolution.

Azure Container Apps, Azure SQL, Front Door, Key Vault/managed identity, Azure Monitor/OpenTelemetry and storage are the preferred hypothesis. Networking, revision behavior, database tenancy, cost, quotas and IaC ownership are not yet validated.

The [#108 read-only preflight](../spikes/108-azure-workload-provider-preflight.md) confirmed West Europe capacity, provider registration, Bicep compilation, and a low bounded proof cost. It also found that the current Elsa 3.8 Combined image supports SQLite persistence only. Acceptance is therefore blocked on a SQL Server-capable immutable image from [`elsa-production-image#26`](https://github.com/valence-works/elsa-production-image/issues/26) and the subsequent real deployment evidence.

## Proposed Decision

- Use Azure Container Apps for the initial Elsa workload proof unless the bounded spike disproves requirements.
- Use Azure SQL with database/pool topology selected by isolation profile.
- Use Front Door for public edge/routing/TLS when the proof requires managed public ingress.
- Use managed identity and Key Vault for provider/workload secrets; store only safe references in control-plane history.
- Treat a deployment stamp as a provider-owned unit of region, capacity, failure containment and isolation.
- Use checked-in Bicep/provider modules as the production resource-realization source of truth, with Elsa Control provider commands orchestrating idempotent plan/apply/checkpoint behavior. Aspire remains local orchestration/developer experience.
- Defer AKS until a concrete requirement cannot be met safely/economically by Container Apps.

## Evidence Required Before Acceptance

- Actual Elsa endpoint deployed and workflow smoke-tested.
- Repeated apply/no-op, interruption/retry and failed revision evidence.
- Secret/identity and database lifecycle proof.
- Network/private evolution analysis and observable cost/quota data.

## Consequences if Accepted

- Azure provider implementation proceeds in PR-sized resource/lifecycle slices.
- Stamp and placement facts remain below the provider boundary established by ADR-0007.
- The current Elsa Control host infrastructure remains separate from managed Elsa workload resources.
