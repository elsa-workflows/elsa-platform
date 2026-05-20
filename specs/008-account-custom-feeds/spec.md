# Feature Specification: Account-Owned Custom Feeds

**Feature Branch**: `008-account-custom-feeds`

**Created**: 2026-05-18

**Status**: Draft

**Input**: User description: "After source-scoped public browsing, add the path for arbitrary users to sign up or arrive from Lovable/customer identity, store custom package feeds under their own Catalog Service account/workspace, index those feeds, and allow only entitled authenticated users to browse their private indexed sources while anonymous/free users can only toggle public browseable source feeds."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Signed-In User Gets A Catalog Workspace (Priority: P1)

A signed-in user arriving from Lovable or a future customer service is recognized by the Catalog Service through a trusted identity context and receives a catalog-local account with a personal workspace. The user can ask for their workspace context without the frontend inventing or supplying arbitrary user IDs.

**Why this priority**: Custom feeds need durable ownership before source creation, indexing, entitlement checks, or private package visibility can be safe.

**Independent Test**: Can be tested by calling the authenticated "me/workspaces" experience with a trusted identity context and verifying that the same external identity consistently maps to the same account and workspace.

**Acceptance Scenarios**:

1. **Given** a request with a trusted external identity that has no existing catalog account, **When** the user requests their workspace context, **Then** the system creates a catalog account, links the external identity, creates one personal workspace, and returns that workspace.
2. **Given** a request with the same trusted external identity after the first request, **When** the user requests their workspace context again, **Then** the system returns the existing account and workspace without creating duplicates.
3. **Given** a request without a trusted identity context, **When** the user requests account or workspace context, **Then** the system rejects the request and does not create account data.

---

### User Story 2 - Entitled User Adds A Custom Feed Source (Priority: P1)

An authenticated user with the required entitlement can add a NuGet feed URL to a workspace as a private catalog source. The Catalog Service stores the source under that workspace so it can be indexed and used by that workspace's members.

**Why this priority**: This is the core paid/custom-feed capability that makes custom feed URLs useful instead of merely collecting URLs the catalog cannot browse.

**Independent Test**: Can be tested by granting a workspace custom-source entitlement, creating a source through the workspace source endpoint, and verifying the source is private, workspace-owned, and eligible for sync.

**Acceptance Scenarios**:

1. **Given** an authenticated workspace member with permission and entitlement to create custom sources, **When** they submit a valid NuGet feed name and URL, **Then** the system creates a private workspace-owned source and returns its source identifier.
2. **Given** an authenticated workspace member without custom-source entitlement or beyond their source limit, **When** they submit a custom feed, **Then** the system rejects the request with a clear entitlement failure and does not create a source.
3. **Given** an authenticated workspace member submits an invalid or unsupported feed URL, **When** they attempt to create the source, **Then** the system rejects the request with validation details and does not persist the source.

---

### User Story 3 - Workspace Member Browses Public And Private Indexed Sources Together (Priority: P2)

An authenticated workspace member can list their workspace sources alongside public browseable sources and browse packages from any selected mix of public and workspace-owned sources. Anonymous users continue to see only public browseable sources.

**Why this priority**: Once custom feeds exist, the package browser and builder flows must expose indexed private feed packages only to the owning workspace while preserving the existing anonymous/free public experience.

**Independent Test**: Can be tested by creating a workspace-owned source with indexed packages, then confirming authorized members can include it in source filters while anonymous or non-member callers cannot discover or query it.

**Acceptance Scenarios**:

1. **Given** a workspace-owned source with indexed packages and an authenticated workspace member, **When** the member lists available sources, **Then** the response includes public browseable sources and that workspace-owned source.
2. **Given** the same workspace-owned source, **When** an anonymous user lists sources or filters packages by that source ID, **Then** the source and its packages are not returned.
3. **Given** an authenticated workspace member selects both public and workspace-owned source IDs, **When** they browse packages or resolve builder selections, **Then** the system returns only packages visible to that member from the selected sources.

---

### User Story 4 - Operator Seeds Or Updates Workspace Entitlements (Priority: P3)

An operator can grant or change workspace entitlement snapshots so custom-feed capability can be tested and manually enabled before billing or a central customer service exists.

**Why this priority**: Paid plans are not implemented yet, but the catalog needs an internal enforcement point and a practical way to unlock custom feeds during the transition.

**Independent Test**: Can be tested by creating an entitlement snapshot for a workspace, then confirming source creation succeeds or fails according to that snapshot.

**Acceptance Scenarios**:

1. **Given** a workspace with no custom-feed entitlement, **When** an operator grants an entitlement snapshot allowing one source, **Then** the workspace can create one custom source.
2. **Given** a workspace entitlement snapshot is updated to remove custom-source capability, **When** a member attempts to add another source, **Then** source creation is rejected while existing indexed sources remain visible according to workspace visibility rules.

### Edge Cases

- A trusted identity arrives with the same issuer and subject but a changed email or display name; account linkage remains stable and profile metadata is updated without creating a new account.
- A frontend attempts to supply a raw user ID, workspace ID, or account ID without a trusted identity context; the request is rejected.
- A user is a member of multiple workspaces; source-management and package-browse requests require an explicit workspace context when private sources are involved.
- A workspace-owned source is disabled, soft-deleted, or not browseable; it is not returned to workspace source lists or package filters.
- A workspace source URL contains credentials, query tokens, or fragments; public or member-facing responses return a sanitized URL only.
- A workspace-owned source contains the same package ID and version as a public source; package identity remains source-qualified.
- Sync has not run yet for a new source; the source appears in workspace source management with zero indexed packages and a status that makes the absence of packages clear.
- Entitlement limits change after sources already exist; new source creation is blocked when over limit, but existing data is not silently deleted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST create and maintain catalog-local accounts that are independent from Lovable, GitHub, email/password, or a future central customer service.
- **FR-002**: System MUST map trusted external identities by issuer and subject to catalog-local accounts.
- **FR-003**: System MUST reject account, workspace, and workspace-source operations when the request lacks a trusted identity context.
- **FR-004**: System MUST create one personal workspace for a newly provisioned account.
- **FR-005**: System MUST maintain workspace memberships with roles sufficient to distinguish source administrators from read-only members.
- **FR-006**: System MUST allow authenticated users to list workspaces they belong to.
- **FR-007**: System MUST allow entitled workspace source administrators to create private NuGet feed sources owned by that workspace.
- **FR-008**: System MUST validate workspace source names, URLs, supported source types, duplicate source URLs within the same workspace, and unsupported private-credential scenarios before persisting a source.
- **FR-009**: System MUST enforce entitlement snapshots before creating workspace-owned sources, including whether custom sources are allowed and the maximum number of active workspace sources.
- **FR-010**: System MUST provide an operator-controlled way to create or update workspace entitlement snapshots before billing integration exists.
- **FR-011**: Workspace-owned sources MUST be private by default and MUST NOT appear in anonymous public source, package, package detail, builder, or compatibility responses.
- **FR-012**: Authenticated workspace members MUST be able to list public browseable sources and sources visible to their selected workspace.
- **FR-013**: Package listing, package details, builder catalog, builder resolve, and compatibility checks MUST only resolve workspace-owned sources when the caller is authorized for the owning workspace.
- **FR-014**: Package identity MUST remain source-qualified for all public and workspace-owned sources.
- **FR-015**: System MUST sanitize source URLs in all non-operator responses so credentials, query strings, and fragments are not exposed.
- **FR-016**: System MUST keep anonymous and non-entitled users limited to toggling existing public browseable sources; they MUST NOT create or browse arbitrary custom feeds.
- **FR-017**: System MUST support manual sync initiation for an authorized workspace source administrator when the source is enabled and within entitlement policy.
- **FR-018**: System MUST record enough source ownership and entitlement information for a later central customer service or billing system to synchronize accounts, workspaces, and plans without changing package identity.

### Key Entities *(include if feature involves data)*

- **Account**: A catalog-local owner record representing a person or future customer principal; linked to external identities and memberships.
- **External Identity**: A trusted issuer and subject pair, with optional profile metadata, that maps an external sign-in to a catalog account.
- **Workspace**: A durable container for workspace-owned sources, memberships, and entitlement enforcement. Initially each account receives one personal workspace.
- **Workspace Membership**: The relationship between an account and workspace, including role and membership state.
- **Package Source Ownership**: The visibility and ownership classification for a source: catalog-owned public source or workspace-owned private source.
- **Workspace Entitlement Snapshot**: The latest catalog-enforced limits and capabilities for a workspace, such as whether custom sources are allowed and the maximum source count.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time trusted identity can obtain a catalog account and personal workspace in a single request, and repeated requests return the same workspace.
- **SC-002**: An entitled workspace administrator can create a custom source and see it in their workspace source list within one user flow.
- **SC-003**: Anonymous requests and authenticated non-member requests cannot discover or retrieve packages from a workspace-owned source, even when they know its source ID.
- **SC-004**: Package browsing with a selected mix of public and workspace-owned sources returns only packages from sources visible to the caller.
- **SC-005**: Entitlement changes are enforced for new source creation without requiring frontend-only gating.
- **SC-006**: All source URL responses exposed to non-operators omit credentials, query strings, and fragments.

## Assumptions

- Lovable remains the initial frontend and sign-in experience, but the Catalog Service stores durable account and workspace records.
- The first implementation accepts either a trusted OIDC/JWT identity or a trusted server-to-server identity header adapter, but never a browser-supplied user ID.
- Billing and subscription purchase flows are out of scope for this feature; entitlement snapshots may be managed by operators or seed/test APIs first.
- Private feed credentials are out of scope; custom feed support is limited to unauthenticated NuGet feed URLs.
- Workspace sharing can be represented in the model, but the first user-facing path may create only personal workspaces.
- Existing public source browsing remains available to anonymous users exactly as introduced by the source-scoped catalog feature.
