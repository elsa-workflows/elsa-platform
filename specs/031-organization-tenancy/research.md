# Research: Organization Tenancy

## Decision: Make Organization The Customer Tenant Boundary

**Rationale**: A customer needs one durable top-level boundary for membership, customer-level entitlements, billing/customer references, audit policy, and workspace creation. Workspace already carries too much operational meaning to also represent customer identity, billing, and multi-workspace administration cleanly.

**Alternatives considered**:

- Keep Workspace as tenant boundary: rejected because one customer cannot naturally own several isolated workspaces without duplicating tenant-level state.
- Treat Account as tenant boundary: rejected because it fails for teams and organizations with multiple members.
- Rename Workspace to Organization: rejected because existing deployment and catalog features already use workspace as an operational resource boundary.

## Decision: Keep Workspace As Operational Isolation Boundary

**Rationale**: Existing package sources, runtime configurations, deployment applications, environments, engines, artifacts, and deployment history are already scoped by workspace. Preserving this boundary minimizes migration risk and keeps project/customer/environment isolation explicit.

**Alternatives considered**:

- Move all records directly under Organization: rejected because it weakens isolation between projects and makes cross-workspace authorization harder to reason about.
- Add Organization and remove Workspace later: rejected because deployment-facing concepts already need a project-like container between customer and application.

## Decision: Separate Organization Membership From Workspace Membership

**Rationale**: Organization membership is needed for customer-level administration, but it should not automatically expose all workspace data. Explicit workspace membership keeps least privilege clear and testable.

**Alternatives considered**:

- Organization membership grants all workspace access: rejected due to high accidental-disclosure risk.
- Only workspace memberships, no organization membership: rejected because organization administrators need to manage members, workspaces, and customer-level settings before workspace-specific access exists.

## Decision: Backfill One Organization Per Existing Workspace First

**Rationale**: The safest migration preserves existing workspace IDs, resource ownership, authorization expectations, and user-visible behavior. Consolidating multiple existing workspaces into one organization can be offered later as an explicit administrative merge or migration workflow.

**Alternatives considered**:

- Infer shared organizations by account ownership or email domain: rejected because it can merge unrelated customer data incorrectly.
- Require manual migration before access: rejected because it would break existing users.

## Decision: Move Entitlement Authority To Organization Scope With Workspace Overrides Deferred

**Rationale**: Entitlements describe customer-level capabilities and limits. Organization scope matches billing/customer ownership better than workspace scope, while still allowing feature-specific workspace limits later.

**Alternatives considered**:

- Keep all entitlements workspace-scoped forever: rejected because it duplicates customer subscription state across workspaces.
- Implement complex organization and workspace override policy now: rejected as broader than the tenant model foundation.
