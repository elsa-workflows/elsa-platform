# ADR-0007: Separate Elsa Application Desired State from Deployment Providers

## Status

Accepted

## Date

2026-08-29

## Context

Elsa Control already has immutable application artifacts, desired-state revisions, Runtime Builder plans/output templates and remote runtime command delivery. The commercial product adds Azure-managed workloads, customer-owned infrastructure, isolation profiles and deployment stamps.

If Azure resources, image strings or one-container assumptions become the customer application model, other providers and future topologies will require parallel domains. If generated output, direct execution and reconciliation are treated as one operation, lifecycle and failure guarantees become ambiguous.

## Decision

Model desired Elsa application state above providers.

- Elsa release/version policy, topology, features/packages, configuration shape, capacity, networking outcomes, isolation and release policy are explicit, separate dimensions.
- Version identity is catalog data with no fixed cardinality or hard-coded set of major versions; adding 3.9, 4.1, 5.0 or later lines does not change the desired-state schema.
- Resolution produces an exact, reproducible Elsa application plan including immutable image/package identities and compatibility evidence.
- Providers translate that resolved plan into provider plans and durable reconciliation commands.
- Provider-specific identifiers, credentials and infrastructure shapes do not leak into customer intent.
- Generated artifacts, direct execution and continuous reconciliation are distinct provider modes.
- Runtime/provider apply remains remote, consistent with ADR-0004. Elsa Control owns analytical validation/diff, orchestration, history and reported state; it does not pretend a remote target is synchronously applied in-process.
- A topology may contain one or several components. Combined is not encoded as a permanent one-container system invariant.

## Alternatives Considered

### Azure-first instance schema

Rejected because it couples product UX and lifecycle to one provider and makes Docker, Kubernetes and customer-agent targets parallel architectures.

### Reuse runtime image strings as the application model

Rejected because version lifecycle, topology composition, provenance and compatibility need governed identities.

### Make the existing in-process deployment engine own provider apply

Rejected by ADR-0004: the live architecture uses durable remote commands and safe reports.

## Consequences

- A resolved application-plan contract is required before provider implementation.
- Existing Runtime Builder, Package Catalog and Deployment models need explicit adapters/mappings rather than wholesale replacement.
- Azure proof work can evolve into stamps/dedicated profiles without changing upper-level intent.
- Major-version migration is distinct from patch/minor upgrade.
