# Managed Elsa browser proof — updated 2026-09-03

## Status

The four-scenario local real-browser proof passes against the immutable linux/amd64
image selected from an admitted release. The shared Azure deployment is healthy on
the corrected, signed build-133 Combined image and the production Control API is
running the provider-integration merge.
The final public-TLS browser journey is ready but still requires the account owner's
interactive Microsoft Entra sign-in and MFA; the unattended attempts timed out safely
without retaining browser artifacts or one-time handoff material.

## Immutable inputs

| Item | Evidence |
|---|---|
| Control commit | `d4ca768b246ef246bf8524b02543513244f8c002` (#211) |
| Control production deployment | GitHub Actions run `33577353790`; health build `89`, image ID equal to the Control commit |
| Production-image commit | `ed747987d67196a47a1184b44be0ae65b454a9c7` (#45) |
| Production-image workflow | `33693067900` (tests, image scans, signatures, evidence and manifest publication passed) |
| Release ID | `3.8.0-preview.5413-build.133` |
| Release manifest OCI subject | `oci://valenceruntimeimages.azurecr.io/release-manifests/release-manifest@sha256:e7b95887a5a53fbaf85a911a329e2a3050448654d40289125f83c2ba922c6a70` |
| Release manifest signature evidence | `oci://valenceruntimeimages.azurecr.io/release-manifests/release-manifest@sha256:4bd0bfee15885ce09c9741747efea29209725a03645d212f5024b40ac59f5bed` |
| Combined image index | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:05ca5b468a528fa5666adc71f6012eb970e2b91aff1c61c84b8d80c90cfcc4d6` |
| Linux/amd64 image | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:67f78a17e8e3e63ace78977e93e79bbb9466661d0e2ada526e74551c43b130f8` |
| Combined SBOM evidence | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:ee6b301ff797cc63ad9bf9347649d0fa5927668b2c469df209d0b16edfb9c919` |
| Combined provenance evidence | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:2a698ce22f150f831d5bf3a890337b88f1d048bf7476072f68b04a90ee73218e` |
| Combined vulnerability evidence | `valenceruntimeimages.azurecr.io/runtime-combined@sha256:f114ca18b37379e1cfb52e4c7b98cd15ea2dc5217af5589f4e78660cecb5c6af` (`trivy-fixable-critical-high-v1`) |
| Elsa / Studio | `3.8.0-preview.5413` / `3.8.0-preview.1667` |
| Topology | Combined, one Azure Container Apps replica; runtime-local handoff/session state |

## Safe evidence collected

| Scenario | Result | Evidence |
|---|---|---|
| Release admission boundary | Pass | #202 is closed. The producer artifact contract and Control admission contract now agree on the immutable OCI subject and payload binding; no raw manifest or signer identity is retained in the resolved plan. |
| Producer manifest and signatures | Pass | Production-image workflow `33693067900` verified the signed release manifest, Combined image index, architecture manifests, SBOM, provenance and vulnerability-scan evidence before publication. |
| Local Control login | Pass | Isolated Chromium completed the real Keycloak authorization-code flow and returned to `/admin/runtimes`. |
| Local proof fixture | Pass | The fixture migrated an isolated catalog copy, seeded the deterministic instance twice, rejected a conflicting runtime origin, and restored availability and membership state between tests. Direct health projection is a fixture boundary, not provider evidence. |
| Local healthy journey | Pass | Chromium opened the real Combined Studio without a second login, called the protected workflow-definitions API (`200`), logged out (`204`), and then received `401` from the protected API. |
| Callback replay | Pass locally | Reposting the already-consumed callback form returned `400`. This proves callback replay rejection; `ManagedElsaHandoffTests.Replay_is_rejected_atomically` and `ManagedElsaHandoffTests.Concurrent_redeemers_allow_exactly_one_success` prove one-time Control redemption/JTI replay. No callback values were retained. |
| Expired browser state | Pass locally (browser) | Delaying issue beyond the configured one-minute runtime browser-state lifetime caused the callback to fail closed with `400`. |
| Expired Control code | Pass (API contract) | `ManagedElsaHandoffTests.Expired_token_is_rejected` proves expired Control code rejection independently of the browser-state failure. |
| Unavailable instance | Pass locally | The real console rendered `Unavailable` and exposed no Open action after the isolated fixture projected unavailable health. |
| Membership revocation before issue | Pass locally | Disabling membership after Open but before issue returned the stable account-unavailable outcome and did not navigate to the runtime. |
| Combined production configuration | Pass | Production-image #45 fixed the server-side Studio backend default to the container's internal `http://localhost:8080/elsa/api` listener, retained the HTTPS development default, added the HTTP launch-profile override, and passed 64 focused tests plus exact-head CI. |
| Azure runtime | Pass | Container App revision `ca-elsa-managed-proof--0000007` is the sole active Healthy revision with one replica and all traffic. It runs the exact Combined image index above with the deterministic instance/audience binding, exact Control continuation and callback, `ManagedElsa:Handoff:StateLifetime=00:01:00`, the internal backend URL, and forwarded-header processing enabled. |
| Azure Control | Pass | App Service `api-m5uymkuaf222o` is Running, HTTPS-only, healthy on build `89`, and has managed-Elsa handoff enabled with a proof-only signing key. |
| Azure handoff start | Pass | The runtime start endpoint returned `302` to the configured Control continuation with only the expected safe query-key names recorded. Values were not retained. |
| Azure public-TLS browser journey | Pending interactive input | The isolated headed run against revision `0000007` reached Microsoft Entra sign-in and timed out after five minutes without the operator completing sign-in/MFA. The harness deleted its unique transient output directory on exit. |

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

## Reproducible fail-closed preflight

The Azure runner executes these safe checks without reading App Service settings:

```bash
az containerapp show \
  --resource-group rg-valence-runtime \
  --name ca-elsa-managed-proof \
  --query '{revision:properties.latestRevisionName,image:properties.template.containers[0].image,stateLifetime:properties.template.containers[0].env[?name==`ManagedElsa__Handoff__StateLifetime`].value|[0],minReplicas:properties.template.scale.minReplicas,maxReplicas:properties.template.scale.maxReplicas}'

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

The runner compares these results to the exact supplied origins, resource names,
immutable Combined image index and one-minute state lifetime. It exits before opening
Chromium unless the runtime is configured for exactly one replica and that active
revision is exclusively Healthy with exactly one running replica and all traffic,
Control is Running and HTTPS-only on build `89` / commit `d4ca768b`,
and both health requests return HTTP 200 within 20 seconds with the expected safe body.
Do not query or print App Service settings.

## Redaction contract

Evidence must not contain handoff codes, state/verifier values, cookies, bearer
tokens, credentials, private signing keys, connection strings, workflow definitions,
raw customer payloads or customer PII. The Azure harness disables screenshots,
traces and video and deletes its transient output directory on both pass and failure.
