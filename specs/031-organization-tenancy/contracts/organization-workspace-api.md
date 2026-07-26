# Contract: Organization And Workspace API

This contract documents externally visible behavior for organization-aware customer context and workspace management.

## Accepted Customer Identity Contexts

Accepted identity contexts remain the same as the identity-tenancy foundation:

- A valid JWT/OIDC identity token with trusted issuer, audience, expiration, and subject.
- A server-side browser login session created from a valid trusted identity.
- A trusted server-to-server request where the platform authenticates the service independently.

Rules:

- Account, organization, workspace, role, and entitlement claims from the browser are never authoritative.
- Server-side account, organization membership, workspace membership, role, permission, and entitlement records are authoritative.
- Operator identity does not automatically imply customer organization membership.

## GET /api/me/organizations

Returns the authenticated customer's organization and workspace context.

Authentication:

- Customer identity required.

Success response:

```json
{
  "account": {
    "id": "00000000-0000-0000-0000-000000000001",
    "displayName": "Ada Lovelace",
    "email": "ada@example.test"
  },
  "organizations": [
    {
      "id": "00000000-0000-0000-0000-000000000101",
      "name": "Contoso Automation",
      "role": "owner",
      "workspaces": [
        {
          "id": "00000000-0000-0000-0000-000000000201",
          "name": "Valence Control Team",
          "role": "owner"
        }
      ]
    }
  ]
}
```

Rules:

- First use of a trusted identity provisions account, organization, default workspace, organization owner membership, and workspace owner membership.
- Returning use returns existing active organizations and visible workspaces.
- Workspaces where the account lacks workspace membership are omitted unless a future audited organization role explicitly allows listing.
- Missing or invalid customer identity returns `401 Unauthorized`.

## GET /api/organizations/{organizationId}/workspaces

Lists workspaces in an organization visible to the current customer.

Authorization:

- Customer identity required.
- Active organization membership required.
- Normal members see only workspaces where they have workspace membership.
- Organization administrators may see all non-archived workspaces unless disclosure policy says otherwise.

Outcomes:

- Authorized: `200 OK`.
- Missing/invalid identity: `401 Unauthorized`.
- Non-member or hidden organization: `404 Not Found` or `403 Forbidden` according to disclosure policy.

## POST /api/organizations/{organizationId}/workspaces

Creates a workspace under an organization.

Request:

```json
{
  "name": "Customer A",
  "initialMembers": [
    {
      "accountId": "00000000-0000-0000-0000-000000000301",
      "role": "reader"
    }
  ]
}
```

Authorization:

- Customer identity required.
- Organization role that permits workspace creation required.
- Organization entitlement and workspace limits must allow creation.

Rules:

- Workspace name must be unique among active workspaces in the organization.
- Creator receives owner access unless explicitly assigned an equal or stronger workspace role.
- Creation writes organization audit metadata.

Outcomes:

- Created: `201 Created`.
- Missing/invalid identity: `401 Unauthorized`.
- Missing role or entitlement: `403 Forbidden`.
- Duplicate active workspace name: `409 Conflict`.

## PATCH /api/organizations/{organizationId}/workspaces/{workspaceId}

Renames or archives a workspace.

Request:

```json
{
  "name": "Customer A Production",
  "status": "active"
}
```

Authorization:

- Customer identity required.
- Organization workspace-management role or workspace owner role required according to operation policy.

Rules:

- Workspace must belong to `{organizationId}`.
- Archive does not delete workspace-owned resource records.
- Active workspace cannot be left without an owner.

## PUT /api/organizations/{organizationId}/workspaces/{workspaceId}/members/{accountId}

Grants or changes workspace membership for an organization member.

Request:

```json
{
  "role": "reader"
}
```

Authorization:

- Customer identity required.
- Organization role that permits workspace membership management, or workspace owner role where policy allows.

Rules:

- Target account must be an active organization member before workspace access can be granted.
- Workspace must belong to `{organizationId}`.
- Membership changes write organization audit metadata.

## DELETE /api/organizations/{organizationId}/workspaces/{workspaceId}/members/{accountId}

Revokes workspace membership.

Rules:

- Revocation cannot leave an active workspace without an owner.
- Revoked access stops authorizing workspace-owned APIs immediately after persistence.

## Workspace-Scoped Compatibility APIs

Existing routes under `/api/workspaces/{workspaceId}` remain available during transition.

Rules:

- The platform resolves the workspace's owning organization before authorizing the request.
- Customer must have active organization membership and required workspace access.
- Responses may include organization identifiers where clients can safely ignore unknown fields.
- New clients should prefer organization-aware context for workspace selection.

## Public Catalog APIs

Public catalog endpoints remain anonymous where already designed as public.

Rules:

- Public responses include only catalog-owned public browseable data.
- Workspace-owned sources remain hidden from anonymous callers.
- Organization-owned shared sources are out of scope for this feature.
