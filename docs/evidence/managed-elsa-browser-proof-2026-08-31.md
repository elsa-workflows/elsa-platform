# Managed Elsa browser proof — 2026-08-31

## Status

Local candidate proof passed. The isolated browser run authenticates through
Keycloak, issues and redeems the one-time handoff, opens Combined Studio without a
second login, performs an authorized Elsa API operation, rejects callback replay,
revokes the runtime session on logout, and covers unavailable, membership-revoked,
and expired-state failures. The Combined host fix is production-image PR #38 at
`59f326a45937f2fc7ef0740fa41884bad22b9021`; it remains a local candidate until
reviewed, merged, published, admitted, and proven from an immutable digest on Azure.

## Immutable inputs

| Item | Evidence |
|---|---|
| Control branch baseline under proof | `45fcbd58ac51da957c8217ff8ca327f3a6b93001` plus the unmerged #200 harness changes |
| Control deployment workflow | `33349285200` (build/tests/publish passed; startup failure described below) |
| Production-image merge | `d43865d8995f7ea6d6180a416f9322c904cbe9a4` |
| Production-image workflow | `33348438992` |
| Release manifest | `valenceruntimeimages.azurecr.io/release-manifests/release-manifest@sha256:5d4a122af55cbbd6a73bca0930f6c338b0ebf04747acd0f1e8473480a48f4552` |
| Combined image index | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:07d7a96b61446f0fcd3c372ba7615f4587b8bd26380031738f24a8dfe43f1cac` |
| Linux/amd64 image | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:19baefc562e5146fb62fc0024d9582dfba8ed7b94ef714da3e26a25bbff28177` |
| Elsa / Studio | `3.8.0-preview.5413` / `3.8.0-preview.1667` |
| Topology | Combined, single replica with runtime-local handoff/session state |

## Safe evidence collected

| Scenario | Result | Evidence |
|---|---|---|
| Local Control login | Pass | Isolated Chromium completed the real Keycloak authorization-code flow for the documented local user and returned to `/admin/runtimes` without a second login. |
| Local proof fixture | Pass | The fixture migrated an isolated catalog copy, seeded the same deterministic instance twice, rejected a conflicting runtime origin without changing the verified endpoint, and restored availability/membership state. |
| Local Managed Elsa console | Pass | Production console rendered the signed-in customer and the healthy managed instance from real lifecycle and identity stores. The fixture projection is explicitly not provider/provisioning evidence. |
| Local handoff protocol | Pass | Chromium completed runtime start, authenticated Control continuation, issue, form callback, server-to-server redemption and bounded runtime-session issuance. Only safe status outcomes and redirect key names were retained. |
| Combined Studio landing | Pass (local candidate) | Production-image PR #38 shares the bounded ticket store with the host and bridges the valid managed session only to Studio Razor/Blazor endpoints. Chromium opened Studio without a second login. |
| Authorized operation and logout | Pass (local candidate) | Chromium called the real Elsa workflow-definitions API with the managed runtime session (`200`), posted runtime logout (`204`), then the same protected API returned `401`. |
| Callback replay | Pass (local candidate) | Replaying the already-consumed callback form returned the stable local rejection (`400`); no callback values were retained in evidence. |
| Expired browser state | Pass (local candidate) | The live suite delayed issuance past the configured one-minute runtime state lifetime and the callback failed closed with `400`. |
| Unavailable instance | Pass | After the fixture projected `Unknown` lifecycle/health, the real console rendered `Unavailable` and no Open action. |
| Membership revocation before issue | Pass | The fixture disabled the organization membership after Open but before the real issue request; Control returned the stable account-unavailable outcome and did not navigate to the runtime. |
| Azure Combined health | Pass | Container App revision `ca-elsa-managed-proof--0000001` is active/Healthy with one replica; `/health` returned 200. |
| Azure runtime handoff start | Pass | `/managed-elsa/handoff/start` returned 302 to the configured Control continuation with only the expected `instanceId`, `state` and `codeChallenge` query keys. Query values were not retained. |
| Historical Control deployment safety | Failed safely, resolved | The first attempt rolled back after Azure SQL rejected an unsupported filtered-index predicate and restored the known-good Control image. #188 is now closed; this remains historical rollback evidence rather than a current blocker. |
| Full instance journey | Local candidate passed; Azure pending | Publish and admit an immutable Combined image containing #37, rerun the same suite against that digest, and complete the Azure browser journey. |

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

- The complete journey against a newly admitted immutable Azure image containing
  the Combined host-session fix.

## Redaction contract

Evidence must not contain handoff codes, state/verifier values, cookies, bearer
tokens, credentials, connection strings, workflow definitions, raw customer
payloads or customer PII. Screenshots and logs retain only safe UI, immutable
artifact identities, resource names, status codes and stable diagnostic codes.
