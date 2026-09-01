# Managed Elsa browser proof — 2026-08-31

## Status

Local published-image proof passed. The isolated browser run authenticates through
Keycloak, issues and redeems the one-time handoff, opens Combined Studio without a
second login, performs an authorized Elsa API operation, rejects callback replay,
revokes the runtime session on logout, and covers unavailable, membership-revoked,
and expired-state failures. Production-image PR #38 is merged and published from
`704d40b75957c65053e9fe9c89ac374a3c5cba7d`; the same four-scenario suite passes
against the published immutable linux/amd64 image. Azure browser proof remains
pending. Control admission cannot yet consume this producer artifact because the
producer schema and OCI subject/payload digest contract differ from the consumer
contract; #202 records that fail-closed boundary.

## Immutable inputs

| Item | Evidence |
|---|---|
| Control harness merge | `b8d1c804d0af75a8172500e34c47d40bfb39e87f` (#201; build/test and console gates passed) |
| Production-image merge | `704d40b75957c65053e9fe9c89ac374a3c5cba7d` (#38) |
| Production-image workflow | `33460462076` (all image, test, manifest, signature and publication jobs passed) |
| Release ID | `3.8.0-preview.5413-build.113` |
| Release manifest OCI subject | `valenceruntimeimages.azurecr.io/release-manifests/release-manifest@sha256:658f1c43da05015aad7f85c4ac885fb9a81bfac586c4b6a6f4d8553183688d78` |
| Exact release-manifest payload SHA-256 | `sha256:755f9f470aeec9e91d71b3eb955b69694254c1b56c24eef23cbbdcad9f84b6e9` |
| Combined image index | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:23948008a467c41407d1044bc86f2f9626c251255c411c6290ffc3d12128a15d` |
| Linux/amd64 image | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:dd5b9866dba49214bf96df4a38f2e3887f3df894ed357f57cb2a3e83ad998ad0` |
| Elsa / Studio | `3.8.0-preview.5413` / `3.8.0-preview.1667` |
| Topology | Combined, single replica with runtime-local handoff/session state |

## Safe evidence collected

| Scenario | Result | Evidence |
|---|---|---|
| Producer manifest and signatures | Pass | The fail-closed producer verifier accepted the downloaded manifest, and local `cosign verify` accepted the exact immutable manifest subject and Combined image index against the recorded GitHub Actions workflow identity and OIDC issuer. |
| Local Control login | Pass | Isolated Chromium completed the real Keycloak authorization-code flow for the documented local user and returned to `/admin/runtimes` without a second login. |
| Local proof fixture | Pass | The fixture migrated an isolated catalog copy, seeded the same deterministic instance twice, rejected a conflicting runtime origin without changing the verified endpoint, and restored availability/membership state. |
| Local Managed Elsa console | Pass | Production console rendered the signed-in customer and the healthy managed instance from real lifecycle and identity stores. The fixture projection is explicitly not provider/provisioning evidence. |
| Local handoff protocol | Pass | Chromium completed runtime start, authenticated Control continuation, issue, form callback, server-to-server redemption and bounded runtime-session issuance. Only safe status outcomes and redirect key names were retained. |
| Combined Studio landing | Pass (published image) | Production-image PR #38 shares the bounded ticket store with the host and bridges the valid managed session only to Studio Razor/Blazor endpoints. Chromium opened Studio without a second login from the published immutable image. |
| Authorized operation and logout | Pass (published image) | Chromium called the real Elsa workflow-definitions API with the managed runtime session (`200`), posted runtime logout (`204`), then the same protected API returned `401`. |
| Callback replay | Pass (published image) | Replaying the already-consumed callback form returned the stable local rejection (`400`); no callback values were retained in evidence. |
| Expired browser state | Pass (published image) | The live suite delayed issuance past the configured one-minute runtime state lifetime and the callback failed closed with `400`. |
| Unavailable instance | Pass | After the fixture projected `Unknown` lifecycle/health, the real console rendered `Unavailable` and no Open action. |
| Membership revocation before issue | Pass | The fixture disabled the organization membership after Open but before the real issue request; Control returned the stable account-unavailable outcome and did not navigate to the runtime. |
| Azure Combined health | Pass | Container App revision `ca-elsa-managed-proof--0000001` is active/Healthy with one replica; `/health` returned 200. |
| Azure runtime handoff start | Pass | `/managed-elsa/handoff/start` returned 302 to the configured Control continuation with only the expected `instanceId`, `state` and `codeChallenge` query keys. Query values were not retained. |
| Historical Control deployment safety | Failed safely, resolved | The first attempt rolled back after Azure SQL rejected an unsupported filtered-index predicate and restored the known-good Control image. #188 is now closed; this remains historical rollback evidence rather than a current blocker. |
| Published immutable local journey | Pass | The four-scenario Playwright suite passed in 2.2 minutes against the newly published linux/amd64 digest. An initial derived-image build failed without retained diagnostics; after an explicit immutable pull, the exact digest built and the complete suite passed. |
| Release admission compatibility | Failed safely; #202 open | Producer publication and local cosign verification use the signed OCI subject, while Control currently conflates that digest with the raw JSON payload digest and expects an older schema shape. No artifact was falsely recorded as admitted. |
| Full instance journey | Published local proof passed; admission and Azure pending | Resolve #202, admit the producer artifact, deploy the same immutable Combined image, and complete the Azure browser journey. |

## Azure resources

- Control: resource group `rg-valence-control-prod`, App Service
  `api-m5uymkuaf222o`, Belgium Central.
- Runtime proof: resource group `rg-valence-runtime`, Container App
  `ca-elsa-managed-proof`, environment `cae-elsa-managed-proof`, Belgium Central.
- Runtime image pull uses the dedicated identity `id-elsa-managed-proof` with
  `AcrPull` scoped to the runtime registry.
- The temporary SQL firewall rule used during diagnosis was removed.
- Control was restored to the known-good image
  `valence-control/api-valence-control-prod:azd-deploy-1786839398` after the
  failed migration; health returned 200.

## Required completion evidence

- Successful producer-artifact admission after #202 proves the OCI subject binds the
  exact manifest payload and maps the producer `1.0.0` contract.
- The complete journey against the same newly admitted immutable Azure image containing
  the Combined host-session fix.

## Redaction contract

Evidence must not contain handoff codes, state/verifier values, cookies, bearer
tokens, credentials, connection strings, workflow definitions, raw customer
payloads or customer PII. Screenshots and logs retain only safe UI, immutable
artifact identities, resource names, status codes and stable diagnostic codes.
