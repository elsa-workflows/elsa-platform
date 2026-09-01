# Managed Elsa browser proof

This harness runs the real Control console, Keycloak, Control API, an Elsa 3.8 Combined runtime, and Chromium through the production handoff endpoints. It proves the browser journey without claiming that the production API provisioned the runtime.

The fixture copies the repository's isolated Keycloak catalog, migrates the copy, creates one deterministic managed instance through the production lifecycle service, projects a separately verified healthy endpoint, and creates the production identity binding. The direct health projection is a proof-fixture boundary only; it is not deployment or provider evidence.

## Prerequisites

- Docker, .NET 10, Node/npm, Playwright Chromium, `mkcert`, `openssl`, `curl`, and `lsof`.
- The local `mkcert` root must be installed in the macOS trust store.
- Ports 5173, 5220, 7094, 7444, and 8080 must be free.
- Registry access for the pinned runtime image.

Run from the repository root:

```bash
./scripts/managed-elsa-browser-proof/run-local.sh
```

The default image is the immutable admitted amd64 Combined digest used by the first Elsa 3.8 proof. To validate a replacement candidate, pass an immutable digest or a deliberately local pre-release image:

```bash
MANAGED_ELSA_PROOF_RUNTIME_IMAGE=registry.example/runtime-combined@sha256:<digest> \
  ./scripts/managed-elsa-browser-proof/run-local.sh
```

Only an immutable admitted digest is acceptable for recorded milestone evidence. A local image is useful for pre-publication regression work but is not release evidence.

## Safety and cleanup

The script creates a unique temporary directory, Docker network, containers, Compose project, and derived trust image. Its trap targets those exact names, stops the two background development processes, removes the fixture-owned Docker resources and Keycloak volume, and deletes the temporary database, keys, certificates, and logs.

Runtime and Control signing material is generated per run and is never printed. The live Playwright spec disables trace capture because the callback form contains one-time security material. It retains only test names and pass/fail outcomes; it does not inspect or print token, verifier, cookie, callback form, credential, workflow payload, or browser storage values.

Playwright ignores local HTTPS errors because its bundled Chromium does not consume the macOS `mkcert` trust state reliably. Browser traffic remains HTTPS, the runtime requires Secure cookies, and the runtime-to-Control redemption client validates the fixture CA through a derived image. Azure proof must use publicly trusted TLS and must not enable this browser exception.
