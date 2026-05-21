# Contract: Identity And Workspace API

This contract documents externally visible behavior for customer identity, workspace context, and workspace authorization.

## Accepted Customer Identity Contexts

- A valid JWT/OIDC identity token with trusted issuer, audience, expiration, and subject.
- A server-side browser login session created from a valid trusted identity.
- A trusted server-to-server request from Lovable or a future customer service when the platform authenticates the service independently.

## Platform Identity Provider Configuration

Built-in adapter kinds:

- `GenericOidc`
- `MicrosoftEntra`
- `Auth0`
- `Keycloak`
- `Custom`

Rules:

- Provider presets configure defaults only; account/workspace mapping remains based on normalized issuer and subject.
- Subject, display name, and email claim names are configurable.
- Workspace roles and entitlements are never accepted from provider claims as authority.

## Rejected Customer Identity Contexts

- Browser-supplied user IDs.
- Browser-supplied account IDs.
- Browser-supplied workspace membership, role, or entitlement claims.
- Email address alone.
- Unsigned or unverifiable user context.
- Trusted-header development identity when production trusted-header mode is disabled.

## GET /api/me/workspaces

Returns the current authenticated customer's platform context.

Authentication:

- Customer identity required.
- Operator fallback identity is not sufficient unless it is also a customer identity.

Success response:

```json
{
  "account": {
    "id": "00000000-0000-0000-0000-000000000001",
    "displayName": "Ada Lovelace",
    "email": "ada@example.test"
  },
  "workspaces": [
    {
      "id": "00000000-0000-0000-0000-000000000101",
      "name": "Ada Lovelace",
      "kind": "personal",
      "role": "owner"
    }
  ]
}
```

Rules:

- First use of a trusted identity provisions account, external identity, personal workspace, and owner membership.
- Repeated use returns existing records.
- Profile metadata updates do not change account identity.
- Missing or invalid customer identity returns `401 Unauthorized`.

## Workspace-Scoped Customer APIs

All customer-owned APIs that include `/api/workspaces/{workspaceId}` share these rules.

Authorization:

- Customer identity is required.
- Active membership in `{workspaceId}` is required.
- Operation-specific role is required for privileged reads or mutations.
- Operation-specific entitlement is required when the feature is entitlement-gated.

General outcomes:

- Authorized read: `200 OK`.
- Authorized create: `201 Created` or operation-specific success response.
- Missing/invalid customer identity: `401 Unauthorized`.
- Authenticated non-member: `403 Forbidden` or not-found-equivalent where the endpoint must avoid revealing resource existence.
- Missing role or entitlement: `403 Forbidden` with a non-secret problem detail.
- Workspace soft-deleted: `404 Not Found` or `403 Forbidden` according to the endpoint's existing disclosure policy.

## Operator APIs

Operator-only APIs remain under the existing admin authorization surface unless a future spec defines a replacement.

Rules:

- Customer identity alone is not sufficient.
- Operator identity does not automatically imply workspace membership.
- Operator entitlement management may update workspace entitlement snapshots.
- Operator actions should produce audit metadata that distinguishes operator authorization from platform identity authorization.

## Public Catalog APIs

Public catalog endpoints remain anonymous where already designed as public.

Rules:

- Public responses include only catalog-owned public browseable data.
- Workspace-owned sources, packages, package versions, and compatibility results are omitted for anonymous callers.
- Authenticated workspace variants may combine public catalog-owned data with workspace-owned data only after membership is verified.
