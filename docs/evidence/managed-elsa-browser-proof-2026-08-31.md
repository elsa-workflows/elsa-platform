# Managed Elsa browser proof — updated 2026-09-02

## Status

The four-scenario local real-browser proof passes against the immutable linux/amd64
image selected from the admitted build-120 release. The shared Azure deployment is
healthy and the production Control API is running the provider-integration merge.
The final public-TLS browser journey is ready but still requires the account owner's
interactive Microsoft Entra sign-in and MFA; the unattended attempts timed out safely
without retaining browser artifacts or one-time handoff material.

## Immutable inputs

| Item | Evidence |
|---|---|
| Control commit | `d4ca768b246ef246bf8524b02543513244f8c002` (#211) |
| Control production deployment | GitHub Actions run `33577353790`; health build `89`, image ID equal to the Control commit |
| Production-image commit | `ac10834873d261b612bb4c921f0d541ce8a328c5` (#40) |
| Production-image workflow | `33469070886` (image, tests, manifest, signatures and publication passed) |
| Release ID | `3.8.0-preview.5413-build.120` |
| Release manifest OCI subject | `valenceruntimeimages.azurecr.io/release-manifests/release-manifest@sha256:75b5026e9dfaf715c4705ccc677512b6aec08395f529b6bf83fd209198d16875` |
| Combined image index | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:80b4f0665bdb51bd8a661b5756164a605a237b70ba315c7f0665c223e81a44cf` |
| Linux/amd64 image | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:c167eac48374cdf653e0bc21b54f50dc22b44f9e8b67f90d1b2c22ff152ebf09` |
| Elsa / Studio | `3.8.0-preview.5413` / `3.8.0-preview.1667` |
| Topology | Combined, one Azure Container Apps replica; runtime-local handoff/session state |

## Safe evidence collected

| Scenario | Result | Evidence |
|---|---|---|
| Release admission boundary | Pass | #202 is closed. The producer artifact contract and Control admission contract now agree on the immutable OCI subject and payload binding; no raw manifest or signer identity is retained in the resolved plan. |
| Producer manifest and signatures | Pass | Production-image workflow `33469070886` verified the signed release manifest, Combined image index, architecture manifests, SBOM, provenance and vulnerability-scan evidence before publication. |
| Local Control login | Pass | Isolated Chromium completed the real Keycloak authorization-code flow and returned to `/admin/runtimes`. |
| Local proof fixture | Pass | The fixture migrated an isolated catalog copy, seeded the deterministic instance twice, rejected a conflicting runtime origin, and restored availability and membership state between tests. Direct health projection is a fixture boundary, not provider evidence. |
| Local healthy journey | Pass | Chromium opened the real Combined Studio without a second login, called the protected workflow-definitions API (`200`), logged out (`204`), and then received `401` from the protected API. |
| Callback replay | Pass locally | Reposting the already-consumed callback form returned `400`. This proves callback replay rejection; `ManagedElsaHandoffTests.Replay_is_rejected_atomically` and `Concurrent_redeemers_allow_exactly_one_success` prove one-time Control redemption/JTI replay. No callback values were retained. |
| Expired browser state and code | Pass locally | Delaying issue beyond the configured one-minute runtime browser-state lifetime caused the callback to fail closed with `400`; `ManagedElsaHandoffTests.Expired_token_is_rejected` proves expired Control code rejection. |
| Unavailable instance | Pass locally | The real console rendered `Unavailable` and exposed no Open action after the isolated fixture projected unavailable health. |
| Membership revocation before issue | Pass locally | Disabling membership after Open but before issue returned the stable account-unavailable outcome and did not navigate to the runtime. |
| Azure runtime | Pass | Container App revision `ca-elsa-managed-proof--0000003` is active and Healthy with one replica and all traffic. It runs the exact Combined image index above with `ManagedElsa:Handoff:StateLifetime=00:01:00`. |
| Azure Control | Pass | App Service `api-m5uymkuaf222o` is Running, HTTPS-only, healthy on build `89`, and has managed-Elsa handoff enabled with a proof-only signing key. |
| Azure handoff start | Pass | The runtime start endpoint returned `302` to the configured Control continuation with only the expected safe query-key names recorded. Values were not retained. |
| Azure public-TLS browser journey | Pending interactive input | The isolated headed run reached Microsoft Entra sign-in and timed out after five minutes without the operator completing sign-in/MFA. The harness deleted its unique transient output directory on exit. |

Unavailable-instance and membership-revocation simulations intentionally remain
local: mutating shared production tenancy is outside this proof. The Azure run is
scoped to prove the non-simulated public-TLS happy journey, replay, expiry and logout
against the same immutable release.

## Azure resources retained for reproducibility

- Control: resource group `rg-valence-control-prod`, App Service
  `api-m5uymkuaf222o`, Belgium Central.
- Runtime proof: resource group `rg-valence-runtime`, Container App
  `ca-elsa-managed-proof`, environment `cae-elsa-managed-proof`, Belgium Central.
- Runtime image pull uses identity `id-elsa-managed-proof` with `AcrPull` scoped to
  the runtime registry.
- The proof runtime uses its default SQLite backend and proof-only `*` runtime
  permission grant. These are milestone-proof choices, not the production tenancy
  baseline.
- Temporary local and SQL firewall resources were removed. Shared production-scoped
  resource groups were not deleted.

## Required completion evidence

- Complete the isolated headed Chromium run with the account owner's normal Entra/MFA
  step, then record only the scenario result and exact immutable/environment facts.

## Reproducible safe preflight

Before an Azure browser run, verify the retained targets without reading app settings:

```bash
az containerapp show \
  --resource-group rg-valence-runtime \
  --name ca-elsa-managed-proof \
  --query '{revision:properties.latestRevisionName,image:properties.template.containers[0].image,stateLifetime:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__StateLifetime`].value|[0]}'

az containerapp revision list \
  --resource-group rg-valence-runtime \
  --name ca-elsa-managed-proof \
  --query '[?properties.active].{name:name,health:properties.healthState,replicas:properties.replicas,traffic:properties.trafficWeight}'

az webapp show \
  --resource-group rg-valence-control-prod \
  --name api-m5uymkuaf222o \
  --query '{host:defaultHostName,httpsOnly:httpsOnly,state:state}'

curl --fail --silent --show-error \
  https://api-m5uymkuaf222o.azurewebsites.net/health
curl --fail --silent --show-error \
  https://ca-elsa-managed-proof.reddesert-17e28fb5.belgiumcentral.azurecontainerapps.io/health
```

The expected runtime image is the exact Combined image index in the immutable-inputs
table, the active revision must be Healthy with one replica and all traffic, the
configured state lifetime must be `00:01:00`, and both HTTPS health requests must
succeed. Do not query or print App Service settings.

## Redaction contract

Evidence must not contain handoff codes, state/verifier values, cookies, bearer
tokens, credentials, private signing keys, connection strings, workflow definitions,
raw customer payloads or customer PII. The Azure harness disables screenshots,
traces and video and deletes its transient output directory on both pass and failure.
