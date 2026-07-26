# Research: Identity And Workspace Tenancy

> **Forward compatibility note**: `specs/031-organization-tenancy` intentionally revisits the tenant-boundary decision and chooses Organization as the customer tenant boundary while preserving Workspace as the operational isolation boundary.

## Decision: Use Workspace As The Valence Control Tenant Boundary

**Rationale**: Existing account-owned custom feed work already models `Account`, `ExternalIdentity`, `Workspace`, `WorkspaceMembership`, and `WorkspaceEntitlementSnapshot`. Saved runtime configurations, BYOC deployment targets, and managed hosting specs all refer to workspace-scoped ownership or authorization. Standardizing on workspace avoids parallel customer, tenant, organization, and account isolation models.

**Alternatives considered**:

- Account as tenant: too narrow because collaboration and organization workspaces are already anticipated.
- External customer-service ID as tenant: couples catalog ownership to a future system and conflicts with the roadmap that treats external customer IDs as references.
- Runtime tenant as platform tenant: mixes platform ownership with Elsa runtime tenant behavior and would pull deferred deployment reconciliation scope into this feature.

## Decision: Use Pluggable Valence Control Identity Adapters

**Rationale**: Valence Control should not force one identity provider. Deployments need to configure Microsoft Entra, Auth0, Keycloak, generic OIDC/JWT, or a trusted backend/customer service adapter while keeping the same `issuer + subject -> Account -> Workspace` mapping. Provider presets should configure validation and claim defaults, but the workspace tenancy model must remain provider-neutral.

**Alternatives considered**:

- Hard-code Microsoft Entra: strong Azure fit, but too restrictive for self-hosted or non-Azure platform users.
- Hard-code Auth0: convenient SaaS identity provider, but also too restrictive.
- Implement OpenIddict as the default identity provider: useful for a future self-hosted identity module, but it would make Valence Control responsible for user credentials, recovery, MFA, abuse prevention, and client management before that is a product requirement.

## Decision: Derive Customer Identity From OIDC/JWT Or Trusted Server-To-Server Context

**Rationale**: The roadmap already accepts verified OIDC/JWT identity or a trusted backend adapter, and rejects browser-supplied user IDs. OIDC/JWT supports direct customer login and future centralized customer identity, while the trusted server-to-server adapter keeps Lovable integration possible during transition.

**Alternatives considered**:

- Keep trusted browser headers as production identity: insecure because a browser can forge user context unless every request is mediated by a trusted proxy boundary.
- Email-only identity: unstable and ambiguous across providers.
- Shared admin key for customers: cannot support per-user audit, workspace isolation, roles, or entitlements.

## Decision: Preserve Admin API-Key Login As Operator Fallback Only

**Rationale**: The current dashboard auth feature intentionally reused the admin API key for fast protection. That path remains useful for operator access and emergencies, but it must not create customer accounts or workspace memberships.

**Alternatives considered**:

- Remove admin key immediately: unnecessary risk while Valence Control identity is introduced.
- Treat admin key as a super-customer identity: weakens audit and breaks tenant isolation semantics.

## Decision: Centralize Workspace Authorization Helpers

**Rationale**: Multiple specs already need workspace authorization: custom feeds, saved runtime configurations, BYOC deployment targets, deployment runs, and managed runtimes. Endpoint-local role checks are easy to drift. A shared resolver/policy layer makes membership, role, entitlement, and current soft-delete checks testable once and reusable.

**Alternatives considered**:

- Keep per-endpoint `GetWorkspaceAccessAsync` calls: workable for the first custom-feed slice but increases the chance of inconsistent checks as endpoints grow.
- Use EF global query filters only: helpful for some persistence paths but insufficient for role, entitlement, public/private mixed catalog queries, and operator bypass rules.

## Decision: Make Server Records Authoritative For Roles And Entitlements

**Rationale**: Tokens may carry useful profile metadata, but workspace role and entitlement state can change independently of token lifetime. Current server-side membership and entitlement records must be checked on each workspace-scoped request.

**Alternatives considered**:

- Trust role or entitlement claims from identity tokens: stale claims could allow access after membership changes or downgrades.
- Frontend-only gating: improves UX but cannot be the enforcement boundary.

## Decision: Defer Runtime Tenant Overlays And Tenant Reconciliation

**Rationale**: The deployment roadmap places first-class tenant reconciliation in a later platform engineering phase. This feature should define the boundary so customer workspace tenancy does not preclude future runtime tenant scopes, but it should not add tenant-scoped manifests or deployment reconciliation behavior.

**Alternatives considered**:

- Add deployment tenant resources now: expands the feature into deployment engine and manifest design before the current engine/API surfaces are ready.
- Ignore runtime tenancy completely: risks conflating workspace with runtime tenant later.
