# Feature Specification: Organization Tenancy

**Feature Branch**: `031-organization-tenancy`

**Created**: 2026-06-02

**Status**: Draft

**Input**: User description: "Add a root-level Organization aggregate above workspaces, make Organization the customer tenant boundary, allow each organization to maintain multiple workspaces, and preserve Workspace as the operational isolation boundary for applications and environments."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish Organization Tenant Context (Priority: P1)

A signed-in customer operates inside an organization tenant, sees the organizations they belong to, and selects a workspace within that organization for day-to-day work.

**Why this priority**: This is the core model change. Every later workspace, entitlement, deployment, and membership decision depends on a reliable organization tenant boundary.

**Independent Test**: Can be fully tested by signing in as a first-time customer, verifying that an organization and default workspace are provisioned, then loading the current organization/workspace context.

**Acceptance Scenarios**:

1. **Given** a first-time trusted customer identity, **When** they sign in, **Then** the system creates one organization, one default workspace in that organization, and owner-level access for the customer.
2. **Given** a returning customer who belongs to multiple organizations, **When** they load their context, **Then** the response lists each organization and the workspaces visible to them within each organization.
3. **Given** a customer selects a workspace, **When** workspace APIs are called, **Then** the system resolves both the owning organization and the workspace before authorizing access.

---

### User Story 2 - Maintain Multiple Workspaces Per Organization (Priority: P2)

An organization administrator creates, renames, archives, and reviews workspaces under the same organization so teams can isolate projects, customers, or deployment domains without creating separate tenants.

**Why this priority**: The business reason for adding organizations is to let one customer tenant contain several operational workspaces.

**Independent Test**: Can be fully tested by creating a second workspace under an organization, granting access to a member, and verifying that both workspaces remain isolated while sharing the same organization tenant.

**Acceptance Scenarios**:

1. **Given** an organization administrator, **When** they create a workspace named "Customer A", **Then** the workspace belongs to that organization and appears in the organization's workspace list.
2. **Given** two workspaces in the same organization, **When** a user has access to only one workspace, **Then** they cannot read or mutate records in the other workspace.
3. **Given** an archived workspace, **When** users list active workspaces, **Then** the archived workspace is hidden from normal selection but remains available for audit and recovery paths.

---

### User Story 3 - Separate Organization Membership From Workspace Access (Priority: P3)

Organization membership grants customer-level capabilities such as organization administration or workspace creation, while workspace membership controls access to workspace-owned resources.

**Why this priority**: Organization-level access must not accidentally expose every workspace's deployment data, packages, artifacts, secrets, or audit records.

**Independent Test**: Can be fully tested by adding a user as an organization member without workspace membership, then verifying they can see allowed organization metadata but cannot access workspace-owned records until granted workspace access.

**Acceptance Scenarios**:

1. **Given** a user is an organization member but not a workspace member, **When** they request workspace-owned data, **Then** access is denied or hidden according to the endpoint's disclosure policy.
2. **Given** an organization administrator grants workspace access to a member, **When** the member reloads context, **Then** the workspace appears with the assigned workspace role.
3. **Given** a workspace administrator is not an organization administrator, **When** they try to manage organization membership or billing-level settings, **Then** the system prevents the action.

---

### User Story 4 - Migrate Existing Workspace Tenancy Safely (Priority: P4)

Existing accounts, personal workspaces, organization-kind workspaces, deployment records, package sources, runtime configurations, artifacts, entitlements, and audit metadata continue to work after each workspace is attached to an organization.

**Why this priority**: The model change must preserve current behavior before organization-level improvements are enabled.

**Independent Test**: Can be fully tested by seeding existing workspaces and memberships, running the migration path, and verifying every workspace has exactly one organization while existing workspace-scoped APIs still return the same resources for authorized members.

**Acceptance Scenarios**:

1. **Given** existing workspaces without an organization, **When** organization tenancy is introduced, **Then** each workspace is attached to an organization without changing workspace resource ownership.
2. **Given** existing workspace memberships, **When** migration completes, **Then** equivalent organization and workspace access exists for current owners and members.
3. **Given** existing workspace IDs in API clients, **When** compatibility routes are used during transition, **Then** authorized calls continue to work while new organization-aware routes are available.

### Edge Cases

- A customer belongs to multiple organizations and has different roles in each; authorization uses the selected organization and workspace, not a global role.
- An organization member knows a workspace ID in the same organization but lacks workspace membership; the workspace remains inaccessible.
- Two organizations want workspaces with the same display name; names only need to be unique within an organization.
- A workspace has no owner after migration or membership changes; the system must prevent or repair ownerless active workspaces.
- Legacy `WorkspaceKind.Organization` records exist; they must migrate without becoming nested organization records with ambiguous meaning.
- Organization archival must not delete workspace data or deployment history implicitly.
- Missing organization entitlement data must fail closed for organization-gated actions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST introduce Organization as the root customer tenant boundary for customer-owned platform data.
- **FR-002**: System MUST require every active workspace to belong to exactly one organization.
- **FR-003**: System MUST preserve Workspace as the operational isolation boundary for workspace-owned resources, including package sources, runtime configurations, deployment applications, environments, engines, artifacts, and deployment records.
- **FR-004**: System MUST allow one organization to contain multiple active workspaces.
- **FR-005**: System MUST model organization membership separately from workspace membership.
- **FR-006**: System MUST model organization roles separately from workspace roles.
- **FR-007**: System MUST prevent organization membership alone from granting access to workspace-owned resources unless an explicit organization role is intentionally defined as workspace-wide and audited.
- **FR-008**: System MUST let organization administrators create, rename, archive, and list workspaces within their organization.
- **FR-009**: System MUST let organization administrators grant and revoke workspace access for organization members.
- **FR-010**: System MUST include organization context in customer session/workspace context responses so clients can present organization and workspace selection accurately.
- **FR-011**: System MUST continue rejecting browser-supplied account, organization, workspace, role, and entitlement identifiers as authority.
- **FR-012**: System MUST migrate existing workspaces into organizations without changing existing workspace resource identifiers.
- **FR-013**: System MUST migrate existing workspace owners into organization ownership or administration records sufficient to manage the migrated organization.
- **FR-014**: System MUST preserve existing workspace-scoped API behavior during a transition period through compatibility routes or compatibility request handling.
- **FR-015**: System MUST make new organization-aware API routes available for organization management and workspace management.
- **FR-016**: System MUST move customer-level entitlements and limits to organization scope unless a feature explicitly defines a workspace-level override.
- **FR-017**: System MUST keep operator-only administration separate from customer organization administration.
- **FR-018**: System MUST produce audit metadata for organization membership, workspace creation, workspace archival, entitlement, and cross-workspace administration changes.
- **FR-019**: System MUST deprecate the current meaning of `WorkspaceKind.Organization` so Organization is not confused with a workspace type.
- **FR-020**: System MUST define the platform hierarchy as Organization -> Workspace -> Workflow Application -> Environment for deployment-facing concepts.

### Key Entities *(include if feature involves data)*

- **Organization**: Root customer tenant. Owns workspaces, organization memberships, customer-level entitlements, billing/customer references, and organization audit metadata.
- **OrganizationMembership**: Relationship between an account and an organization, with organization-level roles and lifecycle state.
- **Workspace**: Operational container under one organization. Continues to own workspace-scoped product data and deployment resources.
- **WorkspaceMembership**: Relationship between an account and a workspace, with workspace-level roles and permissions.
- **OrganizationEntitlementSnapshot**: Latest customer-level capabilities and limits for an organization, with optional workspace overrides handled by feature-specific rules.
- **OrganizationAuditRecord**: Safe metadata describing organization-level membership, workspace, entitlement, and administration changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of existing active workspaces have exactly one organization after migration.
- **SC-002**: Existing workspace-scoped resources remain addressable by the same workspace identifiers after migration.
- **SC-003**: A signed-in customer can distinguish organization selection from workspace selection without reading documentation.
- **SC-004**: An organization administrator can create a second workspace and grant a member access in under 2 minutes.
- **SC-005**: Authorization tests prove organization membership alone does not expose workspace-owned records.
- **SC-006**: Cross-organization and cross-workspace access attempts are denied before any private resource data is returned.
- **SC-007**: Existing deployment hierarchy reads as Organization -> Workspace -> Workflow Application -> Environment in user-facing and API-facing documentation.

## Assumptions

- Organization is the customer, billing, entitlement, and top-level security boundary.
- Workspace remains the project/product/deployment isolation boundary inside an organization.
- Existing personal workspaces migrate into a one-workspace organization owned by the same account.
- Existing organization-kind workspaces migrate into an organization with a default workspace unless a later clarification selects a different migration mapping.
- Organization-wide shared assets such as shared deployment tier catalogs, shared package sources, shared secrets providers, and centralized approval policies are future features unless explicitly included here.
- Runtime tenant overlays remain nested deployment/runtime concerns and do not replace Organization or Workspace boundaries.
