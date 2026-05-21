# Quickstart: Identity And Workspace Tenancy

## Local Platform Identity Smoke Test

1. Configure `Authentication:PlatformIdentity` with `Provider = GenericOidc` and a local test issuer/audience/signing key.
2. Start the Package Catalog API.
3. Call `GET /api/me/workspaces` with a valid trusted identity.
4. Confirm the response contains one account and one personal workspace.
5. Call the same endpoint again with the same issuer and subject.
6. Confirm the same account and workspace IDs are returned.

Expected result: first sign-in provisions customer context exactly once, and repeated sign-in is idempotent.

## Provider Integration Smoke Test

1. Configure `Authentication:PlatformIdentity:Provider` for `MicrosoftEntra`, `Auth0`, `Keycloak`, or `Custom`.
2. Configure authority, audience, issuer, and claim mappings for that provider.
3. Sign in and call `GET /api/me/workspaces`.

Expected result: provider-specific configuration changes token validation and claim mapping, but account/workspace provisioning still uses the same issuer and subject contract.

## Invalid Identity Smoke Test

1. Call `GET /api/me/workspaces` without a customer identity.
2. Call it again with an expired, wrong-audience, or untrusted identity.

Expected result: both requests return unauthorized and no account/workspace records are created.

## Cross-Workspace Isolation Smoke Test

1. Create or seed user A with workspace A.
2. Create or seed user B with workspace B.
3. Create a workspace-owned source or saved runtime configuration in workspace A.
4. Call the workspace A resource endpoint as user A.
5. Call the same endpoint as user B, including the exact workspace ID and resource ID.

Expected result: user A succeeds, user B receives an authorization failure or not-found-equivalent response without private data.

## Role And Entitlement Smoke Test

1. Add owner and reader memberships to the same workspace.
2. Grant the workspace an entitlement for one entitlement-gated operation.
3. Verify the owner can perform the operation.
4. Verify the reader cannot perform the privileged mutation.
5. Remove or exhaust the entitlement.
6. Verify the owner is denied for the entitlement-gated operation.

Expected result: role and entitlement checks are enforced server-side.

## Operator Fallback Smoke Test

1. Authenticate with the existing operator/admin path.
2. Call an operator-only entitlement management endpoint.
3. Confirm the operator action succeeds.
4. Call `GET /api/me/workspaces` with only the operator fallback credential.

Expected result: operator-only work remains available, but the operator fallback credential does not create a customer account or workspace membership.
