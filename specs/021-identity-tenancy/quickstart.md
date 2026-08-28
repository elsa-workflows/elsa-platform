# Quickstart: Identity And Workspace Tenancy

## Local Elsa Control Identity Smoke Test

1. Configure `Authentication:ControlIdentity` with `Provider = GenericOidc` and a local test issuer/audience/signing key.
2. Start the Elsa Control API.
3. Call `GET /api/me/workspaces` with a valid trusted identity.
4. Confirm the response contains one account and one personal workspace.
5. Call the same endpoint again with the same issuer and subject.
6. Confirm the same account and workspace IDs are returned.

Expected result: first sign-in provisions customer context exactly once, and repeated sign-in is idempotent.

## Provider Integration Smoke Test

1. Configure `Authentication:ControlIdentity:Provider` for `MicrosoftEntra`, `Auth0`, `Keycloak`, or `Custom`.
2. Configure authority, audience, issuer, client ID, redirect URI, post-logout redirect URI, scopes, and claim mappings for that provider.
3. Open `/admin/runtime-builder` in a browser.
4. Confirm anonymous browser navigation starts customer sign-in through `/api/auth/login`.
5. Complete provider sign-in and return through `/api/auth/callback`.
6. Confirm `GET /api/auth/session` reports `authenticated = true`.
7. Confirm the console can call `GET /api/me/workspaces`.
8. Submit customer sign-out through `/api/auth/logout`.
9. Confirm subsequent workspace API calls require a new customer identity.

Expected result: provider-specific configuration changes token validation and claim mapping, but account/workspace provisioning still uses the same issuer and subject contract.

## API Bearer Token Smoke Test

1. Configure `Authentication:ControlIdentity` with trusted issuer and audience.
2. Call `GET /api/me/workspaces` with a valid `Authorization: Bearer` token.
3. Call a workspace read endpoint with the same bearer token.
4. Call a workspace mutation endpoint with the same bearer token and no browser origin headers.

Expected result: direct API clients can use bearer tokens without browser session cookies or same-origin headers, while workspace membership, role, and entitlement checks remain authoritative.

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

## Verification Results

- `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter "FullyQualifiedName~WorkspaceProvisioningTests|FullyQualifiedName~CustomerAuthenticationTests|FullyQualifiedName~ControlIdentityTests|FullyQualifiedName~WorkspaceIsolationTests|FullyQualifiedName~WorkspaceAuthorizationTests|FullyQualifiedName~OperatorAuthorizationTests"`: passed, 49 tests.

## Scope Notes

- The automated smoke run covers local Generic OIDC/JWT identity, bearer token access, invalid identity rejection, workspace isolation, role and entitlement enforcement, and operator/customer separation.
- Live Microsoft Entra, Auth0, Keycloak, or custom IdP browser sign-in remains environment-specific and should be repeated when provider credentials and callback URLs are available.
