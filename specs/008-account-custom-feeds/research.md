# Research: Account-Owned Custom Feeds

## Decision: Catalog-Local Accounts With External Identity Mapping

Use catalog-local `Account` records as durable owners and map trusted external identities by `issuer + subject`.

**Rationale**: Lovable is the first sign-in surface, but the catalog should not make Lovable user IDs or email addresses its primary identity. `issuer + subject` is stable across email/profile changes and can later map identities from a central customer service.

**Alternatives considered**:

- Store Lovable user IDs directly on sources: rejected because it couples catalog ownership to the current frontend.
- Use email address as identity: rejected because email changes and provider collisions create ownership bugs.
- Wait for the central customer service: rejected because custom feeds need an ownership model now.

## Decision: Personal Workspace Per Account First

Provision one personal workspace for each new account and model memberships from the beginning.

**Rationale**: Personal workspaces deliver the first custom-feed flow while preserving a clean path to shared or organization workspaces later.

**Alternatives considered**:

- Put sources directly under accounts: rejected because future sharing and customer-service organization mapping would require rewriting ownership.
- Build organization workspaces now: rejected as broader than the current product slice.

## Decision: Trusted Identity Adapter Before Full OIDC

Introduce a workspace-user identity abstraction and a development/test trusted-header adapter. Full OIDC/JWT validation can be added behind the same boundary later.

**Rationale**: The current repository has admin API key and dashboard cookie auth but no customer-facing OIDC setup. A narrow trusted adapter lets tests and Lovable server-to-server integration proceed without accepting browser-supplied user IDs.

**Alternatives considered**:

- Add complete OIDC validation now: rejected because issuer/audience/customer-service details are not finalized.
- Accept `userId` directly from the browser: rejected by the security requirements.

## Decision: Workspace Entitlement Snapshots Enforced In Catalog

Store the latest entitlement snapshot per workspace and enforce it before source creation.

**Rationale**: Billing can be added later, but catalog behavior must not rely on frontend gates. Operator-managed snapshots provide a manual bridge for development and early paid-user enablement.

**Alternatives considered**:

- Hard-code paid/free behavior in the frontend: rejected because API callers could bypass it.
- Block all custom sources until billing exists: rejected because the user explicitly wants to build the path now.

## Decision: Extend PackageSource Ownership

Add optional workspace ownership fields to `PackageSource` and keep catalog-owned public sources represented by null workspace ownership.

**Rationale**: Existing sync, package, approval, and public query code already centers on `PackageSource`. Extending the model preserves source-qualified package identity and reuses indexing.

**Alternatives considered**:

- Create a separate custom-source table: rejected because indexing packages from two source types would duplicate sync and query paths.
- Store workspace source metadata outside the catalog database: rejected because visibility enforcement must happen in the same query boundary as package data.

## Decision: Workspace-Aware Public Catalog Queries

Keep anonymous public APIs unchanged and add workspace-aware endpoints that pass a workspace visibility context to the same query service.

**Rationale**: This avoids duplicating package projection code while making visibility explicit. Existing source filters continue to work for anonymous public browsing.

**Alternatives considered**:

- Add workspace packages to `/api/packages` automatically when signed in: rejected because it changes anonymous public contract semantics and makes cache behavior harder to reason about.
- Create a wholly separate package query stack: rejected as unnecessary duplication.
