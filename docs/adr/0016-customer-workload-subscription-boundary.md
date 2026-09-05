# ADR-0016: Isolate customer workloads in a dedicated Azure subscription

## Status

Accepted — user decision on 2026-09-05. Provisioning and runtime enablement are separate verification gates.

Creation was verified on 2026-09-05 with state `Enabled` in the expected tenant.
Exact deployment identifiers and readback evidence are retained in the private
operations record, not this architectural contract. No provisioner role grants or
runtime enablement were performed as part of subscription creation.

## Context

The production Azure provider creates instance-owned resource groups, managed identities,
role assignments and workload resources. Its current preflight requires subscription-level
provisioning authority. Granting that authority in the subscription hosting Elsa Control
would expose the control-plane infrastructure to the workload provisioner.

## Decision

- Create a new subscription named **Elsa Cloud — Customer Workloads**, dedicated to
  customer runtime resources and their provider-owned supporting infrastructure.
- Do not repurpose the existing **Pay-As-You-Go** subscription. Leave Elsa Control in
  its existing subscription; this decision does not authorize moving its infrastructure.
- Keep the customer-workload subscription in the existing Microsoft Entra tenant.
- Restrict broad workload provisioning and role-management grants to the new subscription.
  Do not grant subscription-wide Contributor, Owner, User Access Administrator or Role
  Based Access Control Administrator in Control's subscription to satisfy provider preflight.
- Any cross-subscription dependency, such as the governed image registry, requires its
  own reviewed resource-scoped grant and verified provider authority. A separate subscription
  is not permission to broaden registry, identity, secret or control-plane access.
- Pin the exact subscription, tenant, managed identity, registry and template authority in
  provider configuration and retained operation fingerprints. Never select a target from
  ambient CLI defaults or silently retarget existing assignments.
- Preserve the initial West Europe region, Dedicated isolation and checked-in Bicep
  decisions in [ADR-0010](0010-initial-azure-workload-platform.md).

## Alternatives considered

- Keeping workloads in Control's subscription was rejected because the current provider
  permission boundary is too broad for that shared scope.
- Reusing the empty Pay-As-You-Go subscription was explicitly rejected by the user.
- Narrower custom roles remain possible future hardening, but must satisfy executable
  provider preflight and lifecycle tests rather than bypass them.

## Consequences and verification

Subscription creation alone does not enable managed hosting. Verify the new subscription
identity, tenant, scoped grants, registered providers, regional quota, cost controls and
exact provider configuration before enabling mutation workers. Existing assignments and
proof resources remain bound to their original authority and must not be migrated by
editing their subscription identifiers.

Validate real managed-identity creation, health, recovery and confirmed deletion against
the new boundary. Public customer-flow acceptance, operational visibility and launch
readiness remain separate gates. This decision does not claim tenant-wide isolation:
identities, administrators and explicitly granted shared dependencies still require review.

Azure role assignments inherit down their configured scope; resource-level assignments
should be used where only one shared resource is needed. See [Azure RBAC scope](https://learn.microsoft.com/en-us/azure/role-based-access-control/scope-overview).
