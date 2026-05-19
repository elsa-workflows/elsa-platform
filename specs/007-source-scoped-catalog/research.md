# Research: Source-Scoped Catalog And Account Roadmap

## Decision: Source ID Is Required For Package Details

**Rationale**: The existing catalog model allows the same package ID to exist in multiple sources. Requiring `sourceId + packageId` makes identity explicit and avoids ambiguous package details, version lists, builder selections, and compatibility checks.

**Alternatives considered**:

- Keep global package detail routes and fail only when duplicates exist. Rejected because this preserves a misleading contract and creates edge-case behavior.
- Use package ID plus source URL. Rejected because URLs may contain sensitive data, can change, and are not stable identifiers.

## Decision: Anonymous And Free Users Browse Indexed Public Sources Only

**Rationale**: A custom feed URL is not useful for browsing unless the catalog has indexed it. Public browsing should therefore be constrained to catalog-owned browseable sources.

**Alternatives considered**:

- Let the UI accept arbitrary custom feed URLs and search NuGet directly. Rejected because it bypasses manifest validation, approval, source diagnostics, and catalog consistency.
- Let arbitrary custom URLs pass through to the catalog without storing them. Rejected because package discovery and indexing are durable catalog responsibilities.

## Decision: Account-Owned Feeds Are Workspace-Scoped

**Rationale**: Custom feeds are resources that may become team-owned and paid-plan scoped. A workspace boundary supports personal use now and organization/team expansion later.

**Alternatives considered**:

- Attach custom feeds directly to user accounts. Rejected because team sharing and organization billing would require migration.
- Attach custom feeds to external Lovable users only. Rejected because the catalog should survive future identity/customer-service changes.

## Decision: Catalog Enforces Entitlements Locally

**Rationale**: Billing or customer systems may decide capabilities, but the catalog owns package-source creation, indexing, and visibility. Local entitlement snapshots keep enforcement available and auditable.

**Alternatives considered**:

- Check a billing/customer service synchronously for every source operation. Rejected because outages and latency would make catalog operations fragile.
- Let Lovable enforce all paid gates. Rejected because API callers could bypass frontend checks unless the catalog also enforces limits.

## Decision: Lovable Is An Initial Auth Surface, Not The Customer Authority

**Rationale**: Today's users sign in through the Lovable-powered site, but a future central customer service may own customers, plans, and workspace membership. The catalog should map verified external identities to internal accounts/workspaces.

**Alternatives considered**:

- Trust browser-provided user IDs from Lovable. Rejected because it is forgeable.
- Make Lovable's user ID the catalog primary key. Rejected because it couples durable catalog ownership to a replaceable frontend/auth surface.

## Decision: Private Feed Credentials Are Deferred

**Rationale**: Private feed credentials require encrypted storage, redaction, rotation, permission boundaries, and audit behavior. That is a separate security-focused feature.

**Alternatives considered**:

- Include credentials in the first custom-feed feature. Rejected because it increases risk and scope before public source-qualified browsing is fixed.
