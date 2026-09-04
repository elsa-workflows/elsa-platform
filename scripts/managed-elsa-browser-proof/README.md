# Managed Elsa browser proof

This harness runs the real Control console, Keycloak, Control API, an Elsa 3.8 Combined runtime, and Chromium through the production handoff endpoints. It proves the browser journey without claiming that the production API provisioned the runtime.

The fixture creates and migrates a fresh catalog from the checked-in Keycloak realm, creates one deterministic managed instance through the production lifecycle service, projects a separately verified healthy endpoint, and creates the production identity binding. The direct health projection is a proof-fixture boundary only; it is not deployment or provider evidence.

## Prerequisites

- Docker, .NET 10, Node/npm, Playwright Chromium, `mkcert`, `openssl`, `curl`, `grep`, `lsof`, `tee`, and `tr`.
- The local `mkcert` root must be installed in the macOS trust store.
- Ports 5173, 5220, 7094, 7444, and 8080 must be free.
- Registry access for the pinned runtime image.

Run from the repository root:

```bash
./scripts/managed-elsa-browser-proof/run-local.sh
```

The browser proof opens the verified runtime through the production handoff, creates and
publishes a fixture-owned workflow with one `WriteLine` activity, executes it to the
terminal `Finished` state, and then verifies replay rejection and runtime-session logout.

The default image is the current immutable admitted amd64 Combined digest for the Elsa 3.8 handoff capability. To validate a replacement candidate, pass an immutable digest or a deliberately local pre-release image:

```bash
MANAGED_ELSA_PROOF_RUNTIME_IMAGE=registry.example/runtime-combined@sha256:<digest> \
  ./scripts/managed-elsa-browser-proof/run-local.sh
```

Only an immutable admitted digest is acceptable for recorded milestone evidence. A local image is useful for pre-publication regression work but is not release evidence.

## Safety and cleanup

The script creates a unique temporary directory, Docker network, containers, Compose project, and derived trust image. Its trap targets those exact names, stops the two background development processes, removes the fixture-owned Docker resources and Keycloak volume, and deletes the temporary database, keys, certificates, and logs.

Runtime and Control signing material is generated per run and is never printed. The live Playwright spec disables trace capture because the callback form contains one-time security material. It retains only test names and pass/fail outcomes; it does not inspect or print token, verifier, cookie, callback form, credential, workflow payload, or browser storage values.

Playwright ignores local HTTPS errors because its bundled Chromium does not consume the macOS `mkcert` trust state reliably. Browser traffic remains HTTPS, the runtime requires Secure cookies, and the runtime-to-Control redemption client validates the fixture CA through a derived image. Azure proof must use publicly trusted TLS and must not enable this browser exception.

## Azure public-TLS journey

The Azure mode reuses the production console and runtime without fixture-only database
mutations. It launches an isolated headed Chromium window and waits for an operator to
complete the real Microsoft Entra sign-in and MFA flow. It does not reuse a personal
browser profile, automate credentials, or retain browser storage, callback values,
tokens, screenshots, traces, or video. Origin inputs may include trailing slashes; the
wrapper canonicalizes them before comparing the named Azure resources.

Azure mode additionally requires an authenticated Azure CLI with read access to the
named App Service and Container App, plus `jq` and `curl`.

```bash
ADMIN_UI_BASE_URL=https://control.example.test \
MANAGED_ELSA_PROOF_CONTROL_RESOURCE_GROUP=rg-control \
MANAGED_ELSA_PROOF_CONTROL_APP_NAME=control-app \
MANAGED_ELSA_PROOF_EXPECTED_CONTROL_IMAGE_ID=<40-character-commit> \
MANAGED_ELSA_PROOF_EXPECTED_CONTROL_BUILD_NUMBER=<build-number> \
MANAGED_ELSA_PROOF_RUNTIME_ORIGIN=https://runtime.example.test \
MANAGED_ELSA_PROOF_RUNTIME_RESOURCE_GROUP=rg-runtime \
MANAGED_ELSA_PROOF_RUNTIME_APP_NAME=runtime-app \
MANAGED_ELSA_PROOF_EXPECTED_IMAGE=registry.example/runtime-combined@sha256:<digest> \
MANAGED_ELSA_PROOF_INSTANCE_ID=<lowercase-canonical-instance-id> \
MANAGED_ELSA_PROOF_STATE_LIFETIME_SECONDS=60 \
  ./scripts/managed-elsa-browser-proof/run-azure.sh
```

Before opening Chromium, the wrapper uses Azure CLI and safe health probes to fail
closed unless the supplied origins match the named resources, the runtime uses the
expected immutable image and state lifetime, exactly one active revision is healthy
with exactly one configured and running replica and all traffic, and Control is running
with HTTPS-only enforcement and the expected commit/build identity. The runtime's
instance ID, audience, Control base URL, Control continuation, and callback URI must
also match the exact proof inputs, and the Combined runtime's server-side backend URL
must be `http://localhost:8080/elsa/api`. ASP.NET Core forwarded-header processing must
be enabled so external redirects retain HTTPS behind Azure TLS termination. Both health probes
must return HTTP 200 within 20 seconds and
the runtime body must be exactly `Healthy`. The proof waits five seconds beyond the
configured bounded state lifetime rather than assuming a hidden default. The wrapper
stores Playwright's transient failure context in a unique temporary directory and
deletes it on exit, whether the proof passes or fails.

After the interactive sign-in, the test opens the verified healthy instance, performs
an authorized Elsa API operation, rejects callback replay, logs out and observes a
`401` from the protected API, then delays a second issue request beyond the bounded
browser-state lifetime and requires the callback to fail closed with `400`.

Unavailable-instance and membership-revocation simulations remain fixture-only checks:
mutating production catalog state is not part of the Azure proof. Evidence must report
those local checks separately from the Azure public-TLS journey.
