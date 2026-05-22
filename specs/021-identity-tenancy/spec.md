# Feature Specification: Identity And Workspace Tenancy

**Feature Branch**: `codex/021-identity-tenancy`

**Created**: 2026-05-21

**Status**: Draft

**Input**: User description: "Proceed with the proposed plan to get multitenancy done and OIDC/JWT login: make Workspace the platform tenant boundary, add real OIDC/JWT login, replace trusted browser-supplied identity with backend-derived account and workspace context, centralize workspace authorization, preserve operator fallback access, and defer runtime tenant reconciliation to a later deployment feature."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign In With Trusted Identity (Priority: P1)

A customer user signs in through a configured platform identity provider adapter, such as generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, or a trusted frontend/backend integration, and is recognized by the platform without supplying account or workspace identifiers directly.

**Why this priority**: Every customer-owned feature depends on a trustworthy user identity before account provisioning, workspace membership, private catalog visibility, saved configurations, deployment targets, or managed runtime ownership can be safe.

**Independent Test**: Can be tested by presenting a valid trusted identity context, requesting the current user's workspace context, and verifying the platform derives account and workspace data from the identity rather than from caller-supplied user IDs.

**Acceptance Scenarios**:

1. **Given** a valid trusted identity with issuer and subject, **When** the user requests their platform context, **Then** the system maps the identity to a catalog-local account and returns the workspaces the account belongs to.
2. **Given** the same trusted identity returns later with changed display name or email, **When** the user requests their platform context, **Then** the system keeps the same account identity and updates profile metadata.
3. **Given** a request without a trusted identity, **When** the caller requests account or workspace context, **Then** the system rejects the request and does not create account, identity, or workspace data.

---

### User Story 2 - First Sign-In Creates A Personal Workspace (Priority: P1)

A first-time signed-in user receives a durable personal workspace that becomes the default tenant boundary for customer-owned platform data.

**Why this priority**: Workspace tenancy must exist before user-owned catalog sources, saved runtime configurations, deployment targets, managed runtimes, and entitlements can share one isolation model.

**Independent Test**: Can be tested by signing in with a new trusted identity twice and verifying that exactly one account, one external identity link, one personal workspace, and one owner membership exist after both requests.

**Acceptance Scenarios**:

1. **Given** a trusted identity with no existing platform account, **When** the user requests their platform context, **Then** the system creates an account, links the external identity, creates one personal workspace, creates an owner membership, and returns that workspace.
2. **Given** two concurrent first sign-in requests for the same trusted identity, **When** both complete, **Then** the system returns the same account and personal workspace without duplicates.
3. **Given** an existing account with multiple workspace memberships, **When** the user requests their platform context, **Then** the system returns every active workspace membership the account may use.

---

### User Story 3 - Enforce Workspace Authorization Everywhere (Priority: P1)

Workspace members can access only customer-owned records in workspaces they belong to, and every workspace-scoped feature uses the same authorization rules.

**Why this priority**: Multitenancy fails if one endpoint or query path can bypass workspace ownership checks, especially when callers know resource IDs.

**Independent Test**: Can be tested by creating two users in separate workspaces, seeding customer-owned records for each workspace, and proving each user can access only their own workspace data across source, package, builder, saved configuration, target, deployment, and managed runtime APIs that are in scope.

**Acceptance Scenarios**:

1. **Given** a workspace member, **When** they read or mutate records owned by their workspace, **Then** the request succeeds only if their membership role allows the operation.
2. **Given** an authenticated non-member who knows another workspace ID or resource ID, **When** they attempt to read or mutate that workspace's records, **Then** the system rejects the request and returns no private data.
3. **Given** an anonymous caller, **When** they browse public catalog data, **Then** public catalog access remains available while workspace-owned data stays hidden.

---

### User Story 4 - Use Role And Entitlement Boundaries (Priority: P2)

Workspace owners and administrators can perform privileged workspace operations, while readers can only inspect data they are entitled to view.

**Why this priority**: Workspace tenancy needs more than membership; source creation, deployment target registration, managed hosting, and future collaboration require stable role and entitlement checks.

**Independent Test**: Can be tested by assigning owner, administrator, and reader memberships in the same workspace, then verifying each role can perform only its allowed operations and entitlement-gated operations fail when the entitlement is absent or exhausted.

**Acceptance Scenarios**:

1. **Given** a workspace owner, **When** they manage workspace-owned sources, saved configurations, or deployment targets, **Then** privileged operations are allowed when relevant entitlements also allow them.
2. **Given** a workspace reader, **When** they attempt a privileged mutation, **Then** the system rejects the mutation while preserving allowed read access.
3. **Given** a workspace without a required entitlement, **When** a member attempts an entitlement-gated operation, **Then** the system rejects the operation server-side even if the frontend displays the action.

---

### User Story 5 - Preserve Operator Access Separately (Priority: P2)

Platform operators retain a separate admin access path for operational and emergency use without making the admin API key a customer login mechanism.

**Why this priority**: The current admin dashboard key flow is useful as an operator fallback, but customer-facing login and workspace tenancy must not depend on a shared admin secret.

**Independent Test**: Can be tested by verifying operator-only admin endpoints remain protected by operator authorization, customer tokens cannot access operator-only functions, and operator credentials do not create customer workspace memberships.

**Acceptance Scenarios**:

1. **Given** an operator uses the existing admin access path, **When** they perform operator-only catalog or entitlement operations, **Then** those operations remain available according to operator authorization.
2. **Given** a customer user with a valid trusted identity, **When** they attempt an operator-only operation, **Then** the system rejects the request.
3. **Given** an operator signs in through the fallback admin path, **When** account workspace APIs are called, **Then** the system does not infer customer account membership from the shared operator credential.

### Edge Cases

- A trusted identity token is expired, has the wrong audience, has an untrusted issuer, or lacks a stable subject; the request is rejected before account or workspace data is read or created.
- The same email address appears under different issuer and subject pairs; account linkage remains based on trusted issuer and subject, not email alone.
- A user is removed from a workspace while their browser still holds a valid session; subsequent workspace requests use current membership and deny access.
- A workspace is soft-deleted; no private records owned by that workspace are exposed or mutable through customer APIs.
- A caller supplies account ID, workspace ID, role, entitlement, or membership claims that conflict with server records; server records are authoritative.
- Workspace source URLs or deployment target metadata contain secrets or tokens; customer-facing responses do not expose raw secrets.
- Public catalog data and health endpoints remain available to anonymous users where already designed as public.
- Runtime tenant manifests and tenant-specific deployment reconciliation are not implemented by this feature; they remain later deployment-platform scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST authenticate customer users from a trusted identity context that provides a verifiable issuer and stable subject.
- **FR-001a**: System MUST expose a pluggable platform identity adapter boundary so deployments can configure Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integrations without changing account/workspace tenancy behavior.
- **FR-001b**: System MUST support configurable claim mapping for subject, display name, and email metadata.
- **FR-002**: System MUST reject browser-supplied user IDs, account IDs, workspace memberships, roles, or entitlements as authority for customer identity.
- **FR-003**: System MUST map each trusted `issuer + subject` pair to one catalog-local account through an external identity record.
- **FR-004**: System MUST create one personal workspace and owner membership for a first-time trusted identity.
- **FR-005**: System MUST make workspace membership the primary platform tenant boundary for customer-owned records.
- **FR-006**: System MUST allow authenticated users to list only active workspaces they are members of.
- **FR-007**: System MUST enforce workspace membership for every customer-owned record read or write.
- **FR-008**: System MUST enforce workspace role requirements for privileged workspace operations.
- **FR-009**: System MUST enforce workspace entitlement snapshots for entitlement-gated operations.
- **FR-010**: System MUST ensure public catalog endpoints expose only public catalog-owned data and never workspace-owned private data to anonymous callers.
- **FR-011**: System MUST ensure authenticated workspace package, source, builder, saved configuration, deployment target, deployment run, and managed runtime operations expose only records visible to the selected workspace.
- **FR-012**: System MUST keep platform identity separate from operator authentication.
- **FR-013**: System MUST preserve an operator-authorized path for platform administration and entitlement management.
- **FR-014**: System MUST prevent customer identities from invoking operator-only operations unless separately granted operator authorization.
- **FR-015**: System MUST update profile metadata from trusted identity context without changing stable account identity.
- **FR-016**: System MUST evaluate workspace membership and role from current server-side records on every workspace-scoped request.
- **FR-017**: System MUST record enough audit metadata to identify the account, external identity, workspace, membership role, and operator/customer authorization path involved in security-sensitive operations.
- **FR-018**: System MUST provide a local or test-only trusted identity mode that cannot be enabled accidentally as a browser-supplied production identity mechanism.
- **FR-019**: System MUST define the boundary between platform workspaces and future runtime tenant/deployment tenant scopes so later features can add nested tenant concepts without changing the account/workspace model.

### Key Entities *(include if feature involves data)*

- **Trusted Identity Context**: Verified sign-in context for a customer user, containing issuer, subject, and optional profile metadata.
- **Platform Identity Provider**: Configured adapter that verifies and normalizes customer identity from Generic OIDC/JWT, Microsoft Entra, Auth0, Keycloak, trusted backend, or custom integration.
- **Account**: Catalog-local user record linked to trusted external identities and workspace memberships.
- **External Identity**: Stable mapping from trusted issuer and subject to one account.
- **Workspace**: Durable tenant boundary for customer-owned platform data; starts as a personal workspace and can support organization workspaces later.
- **Workspace Membership**: Relationship between an account and workspace, including active state and role.
- **Workspace Role**: Permission level for operations within a workspace, such as owner, administrator, source administrator, deployer, or reader.
- **Workspace Entitlement Snapshot**: Server-enforced capability and limit snapshot for a workspace.
- **Operator Principal**: Separate administrative identity used for platform operations and emergency access, not a customer account membership.
- **Customer-Owned Resource**: Any record whose visibility and mutation rights are scoped to a workspace, including private package sources, saved runtime configurations, deployment targets, deployment runs, and managed runtime environments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user with a valid trusted identity can receive an account and personal workspace in a single request, and repeated requests return the same records.
- **SC-002**: Requests lacking trusted identity cannot create or access account or workspace context.
- **SC-003**: Cross-workspace access tests prove a user cannot read or mutate another workspace's customer-owned records even when they know the workspace ID or resource ID.
- **SC-004**: Public catalog browsing continues to work anonymously while workspace-owned sources and packages remain hidden from anonymous users.
- **SC-005**: Role and entitlement tests prove privileged and entitlement-gated operations are denied server-side when the caller lacks the required membership, role, or entitlement.
- **SC-006**: Operator-only operations remain available through operator authorization and are denied to ordinary customer identities.
- **SC-007**: Security-sensitive operations produce audit metadata that distinguishes account/workspace customer actions from operator actions.

## Assumptions

- Workspace is the platform tenant boundary for this feature.
- A customer may belong to multiple workspaces, but personal workspace creation is the first self-service path.
- Organization workspaces, invitations, billing purchase flows, and central customer-service ownership are later features unless already represented by entitlement snapshots.
- Existing account/workspace records from the custom-feed feature are reused and normalized instead of creating a second identity model.
- Existing API-key dashboard access remains an operator fallback while customer login moves to trusted identity.
- Elsa runtime tenant concepts and deployment tenant overlays are separate nested concerns and are intentionally deferred from this feature.
