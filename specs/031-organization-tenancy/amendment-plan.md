# Action Plan: Organization Tenancy Spec Amendments

## Goal

Make `Organization` the root customer tenant boundary while preserving `Workspace` as the operational isolation boundary used by existing catalog, runtime-builder, deployment, artifact, and console features.

## Source Of Truth

`specs/031-organization-tenancy/` becomes the forward-looking source of truth for tenant hierarchy:

```text
Organization
  -> Workspaces
      -> Workflow Applications
          -> Environments
```

Older specs remain useful historical records for the slices they delivered. Where they say "Workspace is the tenant boundary", the new interpretation is:

- Organization is the customer tenant boundary.
- Workspace is the operational/resource isolation boundary inside one organization.
- Workspace-owned records remain workspace-owned unless a later feature explicitly promotes them to organization-owned shared assets.

## Amendment Inventory

| Area | Existing assumption | Required amendment |
|------|---------------------|--------------------|
| `021-identity-tenancy` | Workspace is the platform tenant boundary; organization workspaces are deferred. | Add forward note that `031-organization-tenancy` supersedes the tenant-boundary choice and extends the account/workspace model with organizations. |
| `021-identity-tenancy` API contract | `/api/me/workspaces` returns account plus workspaces only. | Add organization-aware context routes while preserving a compatibility shape for existing workspace clients. |
| `022-deployment-ux` | Deployment records are workspace-owned and workspace is the outer tenant boundary. | Preserve workspace ownership for deployment records, but qualify workspace under organization. |
| `023-engine-health-verification` | Engine verification uses workspace as outer tenant boundary. | Preserve workspace/environment isolation; add organization ownership as authorization context. |
| `024-artifact-registry` and `026-artifact-envelope-and-types` | Artifact records are workspace-owned and workspace is tenant boundary. | Keep artifacts workspace-owned; organization supplies customer tenant and entitlement context. |
| `025-custom-deployment-tiers` | Tier definitions are workspace-owned and organization-wide tier catalogs are deferred. | Keep workspace-owned tiers for this slice; explicitly defer organization-shared tier catalogs to a future feature. |
| Console copy/tests | "Workspace tenant boundary" appears in deployment UI tests/copy. | Replace with organization/workspace hierarchy language during implementation. |
| Persistence and model names | `WorkspaceKind.Organization` can imply organization-as-workspace. | Deprecate or replace with a true `Organization` aggregate and migration mapping. |

## Execution Sequence

1. Create the `031-organization-tenancy` feature spec package with spec, plan, research, data model, API contract, quickstart, and tasks.
2. Add forward-compatibility notes to the high-impact existing specs that currently assert workspace as the tenant boundary.
3. Update the active Spec Kit plan pointer in `AGENTS.md` to `specs/031-organization-tenancy/plan.md`.
4. During implementation, add organization data model and migration first, then update authorization/context APIs, then update console and deployment copy.
5. Keep workspace resource ownership intact until a separate feature explicitly promotes a resource type to organization scope.

## Non-Goals For This Amendment

- Do not implement organization-wide shared package sources, shared deployment tier catalogs, shared secrets providers, or shared approval policies in this feature.
- Do not merge all workspace permissions into organization roles.
- Do not remove workspace-scoped API compatibility until callers have an organization-aware replacement.
- Do not introduce Elsa runtime tenant reconciliation as part of platform organization tenancy.
