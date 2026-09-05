# ADR-0015: Verify governed releases with pinned Sigstore bundles

## Status

Proposed — implementation and production acceptance tracked by #270.

## Context

The governed catalog already separates immutable OCI subject identity from the exact
manifest payload digest and retains safe signature-evidence identities. Its default
host verifier rejects every admission. A production adapter must prove those bindings
without trusting caller-selected signer policy or exposing registry credentials.

## Decision

Keep the existing administrator ingestion API and provider-neutral verifier contract.
An explicitly enabled host adapter reads one server-configured ACR repository using
one explicit user-assigned managed identity. Registry OAuth requests are constructed
from that authority; developer credentials and challenge-selected endpoints are not used.

Independently hash the OCI subject, its selected release-manifest payload layer, the
unique retained Sigstore evidence manifest, and the bundle layer. Require the evidence
manifest to name the exact OCI subject and require the downloaded payload to equal the
caller's exact UTF-8 bytes. Then pass the raw subject and retained bundle to pinned
`cosign verify-blob`, with the host's exact certificate identity and OIDC issuer.
Transparency checks remain enabled. The child receives no registry credential or
ambient environment configuration and returns only a boolean to the adapter.

Pin the cosign binary and Sigstore trusted root by content digest in the API image.
Missing or invalid enabled authority rejects startup. Disabled configuration retains
the existing fail-closed verifier. Valid verification returns only the existing safe
evidence references/digests and transient policy claims, which admission removes before
catalog or plan persistence.

Use a dedicated HTTP client rather than global client-factory defaults: discovery must
not rewrite the pinned registry authority, and retries must not change bounded request
semantics. Disable cookies, proxies, automatic redirects and trace propagation. Suppress
HTTP instrumentation because a permitted blob redirect can contain a SAS query. Permit
at most one blob redirect to an exact configured Azure storage/data hostname, with no
bearer token forwarded. Bound response bytes, descriptors, pages, time and process output.

## Alternatives considered

- A permissive or fixture verifier cannot establish producer provenance and remains
  inappropriate for production admission.
- Passing registry credentials to cosign broadens the child process trust boundary.
  Fetching and digest-checking evidence first keeps crypto execution credential-free.
- Implementing certificate, transparency-log and Sigstore bundle verification ourselves
  would create a larger security-maintenance burden than a pinned upstream verifier.
- Global HTTP client defaults are convenient but cannot guarantee the unchanged
  authority and diagnostic boundaries required for credential-bearing download URLs.

## Consequences

The production host needs narrowly scoped registry read permission, outbound registry
and approved blob-host access, and reviewed binary/root updates. An ACR storage-host
change fails closed until configuration is updated; wildcards are not an escape hatch.
The adapter does not establish vulnerability-policy outcomes or promote preview releases
to supported status. Any number of governed Elsa release lines remains supported.

See [configuration and verification](../production-release-verification.md).
