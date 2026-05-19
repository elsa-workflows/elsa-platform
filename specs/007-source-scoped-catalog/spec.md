# Feature Specification: Source-Scoped Catalog And Account Roadmap

**Feature Branch**: `007-source-scoped-catalog`

**Created**: 2026-05-17

**Status**: Draft

**Input**: User description: "Make the package catalog honest about source/feed selection: anonymous and free users can browse only already-indexed catalog feeds, package details require source identity, and the roadmap should include future account/workspace-owned custom feeds, paid entitlements, Lovable/OpenID Connect integration, and a later central customer service."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse Indexed Sources Only (Priority: P1)

Anonymous visitors and free signed-in users can choose from catalog-owned, already-indexed public package sources and browse only packages from the selected sources.

**Why this priority**: The current custom feed UX can imply that arbitrary feeds are browsable even when the catalog has not indexed them. Correct source-scoped browsing fixes the immediate product mismatch.

**Independent Test**: Seed multiple public sources with distinct approved packages, select one or more source IDs, and verify that package browsing returns only packages from those sources.

**Acceptance Scenarios**:

1. **Given** public catalog sources exist, **When** a visitor requests available browse sources, **Then** only browseable public sources are returned with sanitized feed metadata.
2. **Given** packages exist in two public sources, **When** a visitor browses packages with one selected source, **Then** packages from unselected sources are not returned.
3. **Given** no source is selected, **When** a visitor browses packages, **Then** the catalog returns the default public browse set rather than accepting arbitrary feed URLs.

---

### User Story 2 - Require Source Identity For Package Details (Priority: P1)

Consumers request package details, versions, and specific versions using a source-qualified package identity.

**Why this priority**: The catalog data model permits the same package ID to exist in multiple sources. Requiring source identity avoids ambiguous details and future-proofs private or customer-owned feeds.

**Independent Test**: Seed the same package ID in two sources with different versions or manifests and verify that details are returned only when the requested source ID is supplied.

**Acceptance Scenarios**:

1. **Given** the same package ID exists in two selected sources, **When** a consumer requests details for one source-qualified package, **Then** only that source's package data is returned.
2. **Given** a package exists in a source, **When** a consumer requests package details without a source ID, **Then** the request is rejected or unsupported.
3. **Given** a selected source does not contain the package ID, **When** a consumer requests details, **Then** the catalog returns not found without checking unrelated sources.

---

### User Story 3 - Resolve Builder Selections By Source (Priority: P1)

Runtime Builder catalog and resolve flows include source identity in package selections so generated feed source and package inclusion data is deterministic.

**Why this priority**: Builder output depends on exactly which feed supplied a package. Source-qualified selections prevent silently resolving against the wrong feed.

**Independent Test**: Submit selected packages with source IDs and verify compatibility/resolve behavior uses the matching source-qualified package versions.

**Acceptance Scenarios**:

1. **Given** the builder catalog is requested with selected source IDs, **When** packages are returned, **Then** each package and version includes source provenance.
2. **Given** a builder resolve request includes source ID, package ID, and version, **When** the request is evaluated, **Then** compatibility is checked against that exact source-qualified package version.
3. **Given** a resolve request omits source ID for a package, **When** the request is submitted, **Then** the catalog rejects the request as incomplete.

---

### User Story 4 - Prepare Account-Owned Feed Expansion (Priority: P2)

Signed-in paid users can eventually add package sources under their own account or workspace, and those sources are indexed by the catalog before they become browseable.

**Why this priority**: Custom feeds are only useful if the catalog service indexes them. The ownership and visibility model must be designed before paid custom feed UX is exposed.

**Independent Test**: Create a workspace-owned source in a non-public scope and verify that only authorized members can see the source and its indexed packages.

**Acceptance Scenarios**:

1. **Given** a signed-in paid user has a workspace entitlement for custom feeds, **When** they create a source, **Then** the source is owned by that workspace and is not public.
2. **Given** a workspace-owned source has been indexed, **When** an authorized workspace member browses packages, **Then** public sources and workspace-owned sources can be selected together.
3. **Given** an anonymous visitor requests source lists, **When** workspace-owned sources exist, **Then** none of those private sources are returned.

---

### User Story 5 - Integrate External Identity Without Lock-In (Priority: P2)

The catalog can map identities from the current Lovable-powered site and from a future central customer service to stable catalog accounts or workspaces.

**Why this priority**: The current sign-up surface is external to the catalog. The catalog needs a durable internal ownership model without making Lovable the permanent account authority.

**Independent Test**: Present a verified external identity subject and verify that the catalog maps it to an internal account/workspace without trusting user-supplied identifiers.

**Acceptance Scenarios**:

1. **Given** a verified identity token or trusted backend client context, **When** the catalog receives a signed-in request, **Then** it maps the external subject to an internal account/workspace.
2. **Given** a request supplies only an arbitrary user identifier from an untrusted browser caller, **When** the catalog evaluates the request, **Then** it rejects the identity context.
3. **Given** a future central customer service provides account and entitlement data, **When** the catalog reconciles customer state, **Then** source ownership and package visibility continue to use catalog-local account/workspace identifiers.

### Edge Cases

- A package ID exists in multiple selected sources with different latest versions.
- A selected source is disabled, soft-deleted, private to another workspace, or not browseable.
- A selected source has no approved, listed, valid package versions.
- A public feed URL contains credentials, query parameters, or fragments that must not be exposed.
- A workspace entitlement is downgraded below its current number of custom sources.
- A user signs in with the same email through multiple identity providers.
- Lovable acts as a backend API client but the browser attempts to forge user context.
- A future customer service is unavailable when the catalog needs entitlement data.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a public list of catalog-owned browseable package sources.
- **FR-002**: Public source responses MUST include source ID, display name, sanitized feed URL, and enough metadata for a browse selector without exposing credentials or private configuration.
- **FR-003**: Anonymous and free browsing MUST be limited to package sources already indexed by the catalog.
- **FR-004**: Package list queries MUST accept a set of source IDs and return only approved, listed, valid, non-suspicious packages from those selected sources.
- **FR-005**: Package details, version lists, and version details MUST require source ID and package ID.
- **FR-006**: Package identity in public and builder-facing workflows MUST be source-qualified as source ID plus package ID.
- **FR-007**: Builder catalog queries MUST support filtering by selected source IDs.
- **FR-008**: Builder resolve requests MUST require source ID, package ID, and version for every selected package.
- **FR-009**: Requests for unknown, inaccessible, disabled, deleted, or non-browseable sources MUST not leak private source existence or configuration.
- **FR-010**: The system MUST define an account/workspace ownership model for future custom package sources.
- **FR-011**: Workspace-owned package sources MUST be visible only to authorized workspace members and privileged operators.
- **FR-012**: Custom package sources MUST not become browseable until the catalog has indexed them and applied their include/exclude and approval rules.
- **FR-013**: The system MUST support an entitlement model that can allow or deny custom source creation, source count, package count, sync frequency, and private feed capabilities.
- **FR-014**: The catalog MUST enforce entitlements itself, even when entitlement state originates from an external customer or billing system.
- **FR-015**: The system MUST map external authenticated identities to internal catalog accounts/workspaces using verified tokens or trusted backend client context.
- **FR-016**: The system MUST NOT trust arbitrary browser-supplied user identifiers for account, workspace, source ownership, or entitlement decisions.
- **FR-017**: The identity model MUST support the current Lovable-powered sign-in surface and a future central customer service without changing source-qualified package identity.
- **FR-018**: The first custom-feed scope SHOULD support unauthenticated package feeds only unless private feed credentials are delivered by a separate security-focused feature.
- **FR-019**: Existing admin/operator source management MUST remain separate from end-user workspace-owned source management.
- **FR-020**: Documentation MUST describe the phased path from public source filtering to account-owned paid feed indexing.

### Key Entities *(include if feature involves data)*

- **Browseable Source**: A catalog-owned package source that anonymous or free users may select for package browsing.
- **Source-Qualified Package**: A package identity composed of source ID and package ID.
- **Account**: A catalog-local representation of a person or customer principal that may be linked to external identities.
- **Workspace**: A catalog-local ownership boundary for custom sources, memberships, entitlements, and private package visibility.
- **External Identity**: A verified issuer and subject mapping from Lovable/OpenID Connect or a future customer service to a catalog account.
- **Workspace-Owned Source**: A package source created for a workspace and indexed by the catalog under workspace visibility rules.
- **Entitlement Snapshot**: The catalog's local view of plan capabilities, limits, and paid feature access for a workspace.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A package browsing request with selected source IDs returns zero packages from unselected sources in all tested duplicate-package scenarios.
- **SC-002**: Package detail and builder resolve requests cannot succeed without source-qualified package identity.
- **SC-003**: Public source responses expose no credentials, query strings, or fragments from source feed URLs.
- **SC-004**: Anonymous users can complete source selection and package browsing using only catalog-indexed public sources.
- **SC-005**: The documented roadmap identifies clear implementation phases for public filtering, source-qualified identity, account/workspace ownership, auth integration, entitlements, custom feed indexing, and future customer-service integration.
- **SC-006**: The account model allows at least two external identity providers to map to the same internal account without changing package/source data ownership.
- **SC-007**: Workspace-owned sources are not visible to anonymous users or unrelated workspaces in authorization tests.

## Assumptions

- The current project does not require backward compatibility for global package detail routes.
- Source ID is the canonical source selector and package identity component.
- Anonymous and free users should not create arbitrary custom feed sources.
- Lovable remains the initial user-facing sign-in and UX surface, but the catalog should not permanently depend on Lovable as the customer authority.
- A central customer service may later own customer records, billing state, broader workspace records, and entitlement calculations.
- The catalog remains responsible for package indexing, source ownership, package visibility, and local entitlement enforcement.
- Private feed credentials are deferred to a separate feature because they require dedicated security, storage, rotation, and audit decisions.
