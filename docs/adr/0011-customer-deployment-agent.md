# ADR-0011: Customer-Side Deployment Agent for Elsa Control Cloud

## Status

Proposed

## Date

2026-08-29

## Context

Elsa Control Cloud must manage customer-owned Azure, Kubernetes, Docker or server environments without broad privileged inbound access from Valence. Elsa Control already has an outbound runtime command poll/claim/report pattern, scoped engine credentials and safe diagnostics. That may be a foundation for a customer-side infrastructure/workload reconciler, but current runtime commands primarily apply application artifacts to pre-registered engines.

## Proposed Decision

- Prefer a customer-side agent/reconciler that establishes an outbound authenticated connection to Elsa Control Cloud.
- Reuse the durable command/lease/result pattern where its trust and lifecycle semantics fit.
- Scope every agent identity to an organization, deployment environment and allowed provider capabilities.
- Agents resolve local credentials/secrets and call local infrastructure APIs; Elsa Control stores safe references and reported facts only.
- Enrollment, key rotation, revocation, agent updates, offline behavior, command signing/replay protection, least privilege and support access require explicit design and threat tests.
- Do not introduce an agent if the spike proves existing provider/GitOps mechanisms meet the same trust requirements more safely.

## Alternatives Considered

### Direct inbound Valence access to customer infrastructure

Rejected as the default because private networks, governance and least-privilege requirements make broad inbound control undesirable.

### Treat current runtime applier as a complete infrastructure agent

Rejected without evidence: artifact apply and infrastructure lifecycle have different privilege, drift and recovery responsibilities.

## Evidence Required Before Acceptance

- Enrollment and revocation proof.
- Scoped authorization and replay-resistance tests.
- Offline/reconnect and interrupted-reconciliation behavior.
- Customer Azure/Kubernetes/Docker permission models and update trust.
