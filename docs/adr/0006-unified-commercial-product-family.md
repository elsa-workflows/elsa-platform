# ADR-0006: Use Elsa Control as the Unified Commercial Control Plane

## Status

Accepted

## Date

2026-08-29

## Context

Valence Works needs three commercial operating modes: fully managed Elsa Cloud, a hosted control plane managing customer infrastructure, and customer-operated Elsa Control. A now-deleted `valence-works/elsa-platform-saas` scaffolding repository explored a separate SaaS provisioning control plane. That split would duplicate organizations, entitlements, provisioning and operational state and would make the products diverge.

Elsa Control already owns organizations/workspaces, package governance, desired-state revisions, runtime commands, health, credentials and a shared web console.

## Decision

Elsa Control is the common application control plane for the product family.

- Elsa Cloud is Elsa Control operated by Valence together with Valence-operated Elsa workloads and infrastructure.
- Elsa Control Cloud is Valence-operated Elsa Control managing customer-owned deployment environments.
- Elsa Control Self-Hosted is the same core control platform operated by the customer.
- SaaS-only billing, support and platform-operation modules remain optional capabilities; they do not create a second desired-state or provisioning domain.
- The deleted `elsa-platform-saas` scaffolding is not an implementation or migration source. Commercial lifecycle work is designed directly within explicit Elsa Control boundaries.

Elsa Workflows remains separate OSS. General runtime capabilities may be contributed upstream; Valence SaaS policy does not move upstream merely for convenience.

## Alternatives Considered

### Separate Elsa Cloud control plane

Rejected because it duplicates tenancy, entitlement and deployment state and makes self-hosted/hosted-control capabilities second-class.

### Elsa Cloud as a thin billing shell over unrelated provisioning services

Rejected because customers and operators would still experience multiple authorities for one managed instance lifecycle.

## Consequences

- `elsa-control` is the canonical PRD and program home.
- Product modes require capability composition and deployment configuration, not forks.
- No parallel SaaS repository or migration workstream remains.
- Cross-repository work remains necessary for images, specifications, runtime integrations and websites, but control-plane state is not duplicated.
