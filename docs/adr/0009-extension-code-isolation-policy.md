# ADR-0009: Treat Customer Packages as Executable Code and Gate Them by Isolation

## Status

Accepted

## Date

2026-08-29

## Context

Elsa/Nuplane package installation can execute customer-supplied .NET code with the workload process permissions. Such code can read process state, secrets and files; make network requests; consume resources; probe reachable services; and exfiltrate accessible data.

Package metadata, approval and compatibility checks improve governance but do not create a runtime sandbox.

## Decision

- Define extension policies as built-in, Valence-approved and arbitrary customer packages.
- Arbitrary customer packages are prohibited on Shared and Data-isolated compute unless a separately reviewed sandbox proves an adequate boundary.
- Approved packages require provenance, vulnerability and compatibility policy but do not automatically become safe for every isolation profile.
- Private/custom-code deployment shall use a reproducible immutable artifact/image workflow unless a future security review accepts another model.
- The workflow shall capture exact inputs and digests, validate provenance/security, deploy a revision, validate health and support rollback.
- Secret, network and telemetry access follows least privilege at the workload boundary.

## Alternatives Considered

### Allow arbitrary NuGet packages because tenants are logically separated

Rejected: logical tenant discrimination does not isolate executable code in one process or compute boundary.

### Rely only on package approval

Rejected: approval does not prevent compromise, unsafe behavior or excessive resource use.

### Permanently mutate running containers

Rejected as the managed default because reproducibility, rollback and provenance are weak.

## Consequences

- Any future Shared profile has a constrained extension surface.
- Private/custom-code launch requires a threat model, provenance/build pipeline and isolation tests.
- Entitlements can grant access only to policies that the selected provider/profile actually enforces.

## Evidence and profile gates

The [isolation threat model](../product/isolation-threat-model.md) records the
assets, trust boundaries, explicit profile claims/non-claims, hostile-package
attack paths, Dedicated launch claim matrix and executable promotion gates. It
is the required security evidence register; this ADR does not imply that any
profile or custom-code path has already passed those gates.
