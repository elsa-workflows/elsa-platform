# Managed Elsa browser proof — 2026-08-31

## Status

In progress. The identity, console, runtime image and Azure workload boundaries
have executable evidence. The complete local and Azure instance journeys remain
pending production lifecycle/API composition and the SQL Server migration fix
tracked by #153, #157 and #188.

## Immutable inputs

| Item | Evidence |
|---|---|
| Control commit under proof | `d4c1d7308566c90190809c04e324b053af986f03` |
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
| Local Managed Elsa console | Pass | Production console at Control commit `d4c1d73` rendered the signed-in customer and the empty managed-instance state. No cookies, authorization URLs, codes or tokens were retained. |
| Azure Combined health | Pass | Container App revision `ca-elsa-managed-proof--0000001` is active/Healthy with one replica; `/health` returned 200. |
| Azure runtime handoff start | Pass | `/managed-elsa/handoff/start` returned 302 to the configured Control continuation with only the expected `instanceId`, `state` and `codeChallenge` query keys. Query values were not retained. |
| Control deployment safety | Failed safely | The new exact image pulled, but Azure SQL rejected an unsupported filtered-index predicate in migration `20260830191058_OperateElsaInstanceMigrations`. The migration transaction rolled back and the prior known-good Control image was restored; `/health` returned 200. Fix is #188. |
| Full instance journey | Pending | Production lifecycle/API composition is not yet registered in the API host (#153/#157), so no authoritative ready instance can be created through a supported product path yet. |

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

- Local Control → issue → callback → redeem → runtime session journey.
- One basic authorized workflow operation and runtime logout followed by 401.
- Expired code, replay, membership revocation and unavailable-instance paths.
- The same journey against the immutable Azure image above after #188 and the
  lifecycle/API composition are deployed.

## Redaction contract

Evidence must not contain handoff codes, state/verifier values, cookies, bearer
tokens, credentials, connection strings, workflow definitions, raw customer
payloads or customer PII. Screenshots and logs retain only safe UI, immutable
artifact identities, resource names, status codes and stable diagnostic codes.
