# Contract: Account, Workspace, And Custom Feed Roadmap

This contract is directional for later implementation slices. It defines the product and API boundary without committing the first source-filtering slice to account implementation.

## Identity Trust

Accepted identity contexts:

- A verified OpenID Connect/JWT token with trusted issuer, audience, expiration, and subject.
- A trusted server-to-server request from Lovable or a future customer service that the catalog authenticates independently.

Rejected identity contexts:

- Browser-supplied user IDs.
- Unsigned or unverifiable user context.
- Email address alone.

## Account Mapping

External identity key:

```text
issuer + subject
```

Maps to:

```text
ExternalIdentity -> Account -> WorkspaceMembership -> Workspace
```

## Workspace Source APIs

Future endpoints:

- `GET /api/me/workspaces`
- `GET /api/workspaces/{workspaceId}/sources`
- `POST /api/workspaces/{workspaceId}/sources`
- `PUT /api/workspaces/{workspaceId}/sources/{sourceId}`
- `DELETE /api/workspaces/{workspaceId}/sources/{sourceId}`
- `POST /api/workspaces/{workspaceId}/sync/sources/{sourceId}`

Rules:

- Workspace membership is required.
- Entitlements are checked before source creation and sync.
- Workspace-owned sources are private by default.
- Private feed credentials are not accepted until a separate credentials feature exists.

## Entitlement Snapshot

Minimum capability fields:

```json
{
  "workspaceId": "00000000-0000-0000-0000-000000000001",
  "canCreateCustomSources": true,
  "maxSources": 5,
  "maxPackagesIndexed": 500,
  "maxVersionsPerPackage": 20,
  "maxSyncsPerDay": 25,
  "privateFeedsEnabled": false,
  "syncedAt": "2026-05-17T00:00:00Z"
}
```

Rules:

- Catalog enforces the latest available snapshot.
- External customer or billing systems may calculate entitlements, but catalog operations cannot rely solely on frontend gating.
