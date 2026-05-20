# Roadmap: Source-Scoped Catalog, Accounts, And Paid Feeds

**Feature**: [spec.md](spec.md)

**Created**: 2026-05-17

## Direction

The catalog should treat package identity as `sourceId + packageId` and only let public users browse feeds that the catalog has already indexed. Arbitrary custom feed URLs become useful only when they are stored as catalog sources, indexed by the service, and scoped to an account or workspace.

Lovable can remain the initial UX and authentication surface, but the catalog should avoid treating Lovable as the permanent customer authority. The durable catalog boundary should be internal accounts/workspaces, external identity mappings, source ownership, and entitlement enforcement.

## Phase 1 - Public Source Filtering

Deliver the immediate correctness fix for anonymous and free browsing.

- Add a public browseable-source listing.
- Mark or derive which catalog-owned sources are public and browseable.
- Filter package listing by selected source IDs.
- Filter builder catalog by selected source IDs.
- Ensure public source URLs are sanitized.
- Keep arbitrary custom feed URLs out of anonymous/free browse flows.

Expected outcome: the UX feed selector reflects indexed catalog feeds only, and selected feeds actually constrain package results.

## Phase 2 - Source-Qualified Package Identity

Make source identity mandatory anywhere package details or builder selections are resolved.

- Replace global package detail routes with source-qualified routes.
- Require `sourceId` for package versions and version details.
- Require `sourceId`, `packageId`, and `version` for builder resolve selections.
- Update compatibility and builder flows to resolve exact source-qualified package versions.
- Update tests for duplicate package IDs across sources.

Expected outcome: the same package ID can safely exist in multiple sources without ambiguous API behavior.

## Phase 3 - Lovable UX Alignment

Align the Lovable-built site with the backend contract.

- Populate the feed selector from catalog-provided public sources.
- Persist anonymous/free source selection only as preference state, not as custom catalog sources.
- Hide or gate custom feed creation for non-paying users.
- Submit selected source IDs to catalog/package/builder APIs.
- Display source provenance in package cards and selected package summaries.

Expected outcome: users see only packages from selected indexed feeds, and the UI no longer implies that arbitrary feeds are browsable before indexing.

## Phase 4 - Catalog Account And Workspace Foundation

Create the durable ownership model needed for custom feeds.

- Introduce catalog-local accounts.
- Introduce workspaces, starting with one personal workspace per account.
- Link verified external identities to catalog accounts.
- Add workspace membership.
- Represent source ownership as catalog-owned public source or workspace-owned private source.
- Keep admin/operator source management separate from workspace source management.

Expected outcome: custom feeds have a stable owner independent of a particular frontend or identity provider.

## Phase 5 - Auth Integration

Let the catalog trust signed-in users without trusting arbitrary user IDs.

Preferred path:

- Lovable obtains or forwards verifiable OpenID Connect/JWT identity.
- Catalog validates issuer, audience, expiration, and subject.
- Catalog maps `issuer + subject` to an external identity and internal account/workspace.

Fallback path:

- Lovable backend calls Catalog as a trusted API client.
- Catalog verifies Lovable's service credential.
- Lovable provides user context only over that trusted server-to-server channel.
- Catalog rejects browser-supplied user IDs.

Expected outcome: the catalog can support today's Lovable sign-in while remaining ready for a central customer service.

## Phase 6 - Workspace-Owned Custom Feed Indexing

Allow entitled users to create feeds that the catalog actually indexes.

- Add workspace source CRUD.
- Add workspace source sync controls and diagnostics.
- Apply include/exclude patterns and version discovery policy.
- Keep first custom-feed support limited to unauthenticated feeds unless a private-feed security feature is added.
- Make indexed workspace packages visible only to authorized workspace members.
- Allow authorized users to browse public and workspace-owned sources together.

Expected outcome: paid users can add custom feeds and then browse packages because the catalog has indexed those feeds.

## Phase 7 - Entitlements And Paid Plans

Make paid custom feeds enforceable inside the catalog.

- Add entitlement snapshots for workspaces.
- Enforce whether custom source creation is allowed.
- Enforce source count, package count, version count, sync frequency, and private-feed capability limits.
- Support manual/operator entitlement grants before billing integration exists.
- Later sync entitlement snapshots from Lovable, Stripe, or a central customer service.

Expected outcome: billing or customer systems can grant capabilities, but the catalog enforces package-source limits consistently.

## Phase 8 - Central Customer Service Integration

Prepare for a future shared customer platform.

- Treat customer-service IDs as external references, not primary catalog identity.
- Reconcile customer accounts, workspaces, plans, and entitlements into catalog-local records.
- Continue using catalog-local workspace IDs for source ownership and package visibility.
- Let the customer service own billing, subscriptions, broader organization membership, and entitlement calculation.
- Let the catalog own source configuration, indexing state, package manifests, approval/listing state, and visibility enforcement.

Expected outcome: the account source of truth can move from Lovable to a central customer service without rewriting catalog package identity or source ownership.

## Sequencing Recommendation

Build phases 1 and 2 before exposing any new Lovable custom-feed UI. They fix correctness and prevent global package identity from leaking into later paid-feed work.

Build phase 3 next so the current site behaves honestly for anonymous and free users.

Treat phases 4 through 8 as the paid custom-feed platform. They should be planned as one architecture track but implemented incrementally behind feature gates.

## Non-Goals For The First Implementation Slice

- Anonymous arbitrary feed browsing.
- Private feed credentials.
- Billing-provider integration.
- User-owned feeds visible to other customers.
- Backward-compatible global package detail routes.
