# Runtime Transport Trust Policy

Status: initial production guidance

Last updated: 2026-06-01

Tracking issue: [#50](https://github.com/valence-works/valence-control/issues/50)

## Purpose

This document defines the minimum trust rules for Valence Control integrations that submit or consume deployment artifacts.

The platform stores credential references only. Raw bearer tokens, client secrets, runtime API keys, payload access keys, and webhook signing secrets must remain in host-owned secret stores such as Key Vault, Kubernetes secrets, managed identity, or an equivalent provider.

## Runtime Command Credentials

Runtime command sync uses outbound runtime-to-Valence Control calls for poll, claim, heartbeat, progress, completion, failure, and rejection.

Required properties:

- Credentials are scoped to one workspace and the specific runtime integration role.
- Credentials can read runtime commands, claim commands for registered engines, fetch artifact envelopes, and report command results.
- Credentials cannot manage workspace membership, mutate desired state, register arbitrary artifacts, or perform operator-only actions.
- Credentials are issued as short-lived tokens when possible.
- Long-lived bootstrap credentials are references in the runtime host only and are rotated out after the runtime can acquire normal tokens.

Bootstrap sequence:

1. Register the workflow engine in Valence Control with endpoint metadata, capability metadata, and a credential reference.
2. Create a runtime integration identity in the selected identity provider or secret provider.
3. Store the identity's secret material outside Valence Control.
4. Configure the runtime applier host with `ValenceControl:Auth:CredentialReference`.
5. Start the runtime applier and verify it can poll with no pending commands.
6. Queue a harmless validation or dry-run command and verify the runtime can claim and report it.
7. Rotate any bootstrap-only credential once normal token acquisition is confirmed.

## Rotation Expectations

Credential rotation must not require editing artifact records, desired-state revisions, deployment commands, or deployment history.

Recommended rotation model:

- Use stable secret references in Valence Control-facing configuration, for example `kv://claims/runtime-sync`.
- Rotate the value behind the reference in the secret provider.
- Support overlapping old/new credentials during the rotation window.
- Keep command leases short enough that a worker using an old credential naturally exits or refreshes before the old credential is revoked.
- Verify rotation by polling, claiming a test command, heartbeating, and reporting completion with the new credential.
- Record rotation in operational audit tooling, not in artifact metadata or command diagnostics.

Rotation failure handling:

- If the runtime can no longer authenticate to Valence Control, stop applying commands and keep polling disabled until credentials are repaired.
- If artifact payload access fails after rotation, reject or fail the command with safe diagnostics that do not include provider error bodies containing secrets.
- Do not create replacement deployment commands solely because credentials changed; retry through the existing authoritative command lifecycle.

## Artifact Payload Trust

Artifact payload references are resolved by the runtime applier, not by the platform control plane.

Runtime hosts must:

- Allow only approved payload providers and hosts.
- Verify payload digest before applying an artifact.
- Reject redirects, private-address hosts, expired references, oversized payloads, and unsafe media types unless a host-owned fetcher provides equivalent checks.
- Keep payload access credentials outside Valence Control catalog tables and diagnostics.

## Advisory Webhook Trust

Webhook dispatch is optional and disabled by default. Webhooks are wake-up hints only; they do not transfer deployment authority.

Valence Control sends:

- workspace ID,
- engine ID,
- command hint,
- reason.

Valence Control does not send:

- lease tokens,
- raw workflow content,
- artifact payloads,
- credential material,
- secret values.

Runtime hosts that expose a webhook endpoint must:

- Authenticate or otherwise trust the Valence Control sender through host-owned policy.
- Treat duplicate, delayed, failed, or lost webhook delivery as normal.
- Wake the poll/claim loop instead of applying from the webhook request.
- Return quickly; expensive work belongs in the normal runtime worker.
- Keep polling enabled as the fallback path.

Recommended enablement order:

1. Run runtime pull/sync successfully with webhook dispatch disabled.
2. Add a runtime endpoint that only enqueues a worker wake-up signal.
3. Restrict endpoint access with mTLS, private network policy, gateway auth, or a host-owned signing/authentication handler.
4. Enable `Deployment:WebhookDispatch:Enabled` in Valence Control.
5. Verify duplicate webhook delivery wakes the worker but produces only one command claim.
6. Monitor sent, failed, and skipped notification statuses.

## Safe Diagnostics

Diagnostics may mention stable references and high-level failure categories. They must not include:

- bearer tokens,
- authorization headers,
- client secrets,
- connection strings,
- private keys,
- payload URLs with embedded credentials,
- raw provider error bodies that might contain secret values.

When in doubt, report a short safe message and attach provider-specific details only in the host's protected operational logs.
