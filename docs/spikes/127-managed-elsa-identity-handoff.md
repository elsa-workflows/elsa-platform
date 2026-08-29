# Spike 127 — Managed Elsa identity handoff

**Status:** Prototype contract accepted; production integration remains follow-up work.

## Decision

Use a two-party, short-lived signed handoff code. Elsa Control remains the only
customer identity and organization authority. The managed Elsa runtime does not
create a second customer account or accept a Control session cookie.

1. The browser is already authenticated to Elsa Control through the existing
   `IWorkspaceIdentityReader` seam (OIDC cookie or validated Control JWT).
2. Control resolves the current organization and Elsa Instance authorization from
   its own store. Browser-provided account IDs, roles, organization IDs and
   instance IDs are never trusted as authority.
3. Control signs a one-minute RS256 JWT for the exact managed instance audience and
   exact callback URI. The token is returned only to the authenticated caller.
4. The managed runtime posts the code to the Control redemption seam over TLS,
   supplying its expected audience and callback URI. Control validates the
   signature and claims, atomically consumes the `jti`, and checks current
   organization/instance authorization again.
5. The runtime creates its own short-lived HttpOnly/SameSite session. It never uses
   the handoff code as a long-lived bearer token. Runtime logout revokes that local
   session.

The prototype endpoints are:

```text
POST /api/managed-elsa/handoff/issue
POST /api/managed-elsa/handoff/redeem
```

They are disabled by default and the application registers an explicit deny-all
authorizer until the Elsa Instance aggregate supplies a real implementation. The
test application substitutes an in-memory target authorizer to exercise the
complete protocol without inventing a parallel identity system.

## Token contract

The issuer is the configured HTTPS Control issuer. The audience is an
instance-specific value, such as `urn:elsa:instance:{instance-id}`, and is not a
shared `elsa` audience. The token is accepted only with `RS256` and a known `kid`.
Its claims are:

| Claim | Meaning |
|---|---|
| `iss` | Elsa Control handoff issuer. |
| `aud` | Exact managed Elsa Instance audience. |
| `sub` | Control account identifier, not an Elsa runtime credential. |
| `control_iss` / `control_sub` | Stable source identity used for the second authorization check. |
| `org_id` | Authorized customer organization. |
| `instance_id` | Authorized Elsa Instance. |
| `redirect_uri` | Exact HTTPS (or local development) callback binding. |
| `scope` | Only `runtime:session` in this prototype. |
| `jti` | Unique one-time redemption identifier. |
| `iat`, `nbf`, `exp` | One-minute lifetime; maximum configured lifetime is five minutes. |

No workflow definitions, package payloads, credentials, provider tokens or
runtime API permissions belong in this token.

## Authorization boundary

`IManagedElsaHandoffAuthorizer` is the integration port. Its first method grants
the exact target, audience, redirect URI and scopes for the currently authenticated
`TrustedWorkspaceIdentity`. Its second method rechecks that grant during
redemption. This second check is required because an organization or instance
membership can be revoked after issue and before callback.

The current Control repository does not yet contain the Elsa Instance aggregate
or its persistence/API boundary (#114). Therefore no production authorizer is
guessed here: the default is fail-closed. Once #114 exists, its adapter should
resolve `instance_id` and canonical callback URI from the instance record, verify
that the organization owns the instance, and require the appropriate instance
open/session permission. Existing workspace access may be an input to that rule,
but must not be silently treated as a permanent instance identity.

## Threats and controls

| Threat | Control |
|---|---|
| Stolen front-channel code | One-minute expiry, exact audience and callback binding, TLS redemption, and atomic `jti` consumption. A code is not a runtime session. |
| Code replay/race | `IManagedElsaHandoffReplayStore.TryConsumeAsync` is the first state-changing redemption operation; `ConcurrentDictionary.TryAdd` gives the local prototype an atomic single-use check. Production must use a shared durable conditional insert with expiry. |
| Wrong runtime receives a code | `aud` is instance-specific and is validated against the target's expected audience. |
| Open redirect | Redirect URIs must be absolute HTTPS URIs (localhost HTTP is test-only), with no fragment or user-info. The target authorizer must return the canonical URI; the request is not an allowlist. |
| Revoked organization/instance membership | Current authorization is checked both at issue and redemption. A failed redemption consumes the code and returns a generic authorization failure. |
| Cross-organization confusion | `org_id` and `instance_id` are issued only from an authorization result and are checked as a pair. |
| Privilege escalation | The only prototype scope is `runtime:session`; requested scopes must be a subset of authorizer-granted scopes. Runtime API/admin permissions are not represented. |
| Signing-key compromise or rotation | JWT header carries `kid`; the verifier accepts active plus explicitly retained previous public keys. Production key material belongs in Key Vault/HSM-backed configuration, with overlap during rotation and audit of key ID. |
| Long-lived runtime session | The runtime owns a separate session cookie with bounded lifetime and explicit logout revocation. The handoff JWT is never stored as the cookie. |
| Logout race | Control logout prevents new issuance; runtime logout immediately revokes the local session. A code already issued may still redeem until expiry unless the redemption authorizer observes the revoked Control session/membership. |
| Sensitive audit leakage | Audit events record action, `jti`, account/org/instance IDs, audience and time, never the JWT or credentials. |
| Trusted-header bypass | The prototype reuses the existing identity reader, whose production trusted-header mode is separately proxy-gated; this handoff adds no new header authority. |

## Failure UX

The Control API should return stable, non-sensitive outcomes:

* `401` when there is no authenticated Control identity or the code is invalid or
  expired;
* `403` when current organization/instance authorization is absent or revoked;
* `409` when a valid code has already been redeemed;
* `503` when the handoff feature or production key configuration is unavailable.

The console should preserve the Control session, show “This managed instance is
no longer available” for `403`, “This link has expired; open Elsa again” for
`401` expiry, and retry the normal issue flow once for an expired code. It must not
display the token or disclose whether another organization or instance exists.

## Key rotation and session policy

The prototype key ring supports one active `kid` plus retained validation keys.
Production rotation is: publish the new public key, begin signing with the new
`kid`, retain the old public key for at least the maximum token lifetime plus clock
skew, then retire it. Key changes and failed validation spikes are audit events.

The runtime session lifetime is independent from the code lifetime and should be
bounded by the Control customer session and the runtime's own session policy.
Logout clears/revokes the runtime session. It does not use an OIDC back-channel
logout as a substitute for local session revocation; federation logout remains a
separate enterprise concern.

## Evidence and remaining work

`tests/Hosting/ElsaControl.Api.Tests/ManagedElsaHandoffTests.cs` exercises the
existing ASP.NET Core identity seam and the local protocol implementation. The
tests cover valid issue/redeem, replay, wrong audience, expiry, revoked
membership, cross-organization authorization, redirect mismatch and logout.

This spike does not claim a production-ready Elsa 3.8 runtime hook, durable
replay storage, instance persistence, distributed key management, browser E2E, or
an actual Azure-hosted Combined callback. Those are decomposed into follow-up
Tasks so the walking skeleton cannot silently ship the prototype's in-memory
boundaries.
