# Production release-manifest verification

The existing `POST /api/admin/release-catalog/manifests` endpoint accepts only an
immutable reference, its SHA-256 digest, and the exact manifest payload. Its existing
administrator authorization remains required. Signer policy is owned by the server,
never by this request. The verifier is disabled and fail-closed by default.

## Host configuration

Set `ReleaseCatalog:Verification:Enabled` to `true` only with the complete authority
below. Environment configuration uses double underscores in place of colons.

| Key under `ReleaseCatalog:Verification` | Required authority |
| --- | --- |
| `RegistryHost` | Exact lowercase ACR hostname, without scheme or path |
| `Repository` | Exact governed release-manifest repository |
| `TenantId` | Canonical tenant GUID |
| `ManagedIdentityClientId` | Canonical client GUID of the attached user-assigned identity |
| `BlobRedirectHosts` | Array of exact approved Azure blob/data hostnames; no wildcard |
| `CosignPath` | `/opt/elsa-control/verification/cosign` in the packaged API image |
| `CosignSha256` | Matching binary SHA-256 below, without the `sha256:` prefix |
| `TrustedRootPath` | `/opt/elsa-control/verification/trusted-root.json` |
| `TrustedRootSha256` | Pinned trust-root SHA-256 below, without prefix |
| `RequestTimeoutSeconds` | Optional; default 30, maximum 120 |
| `VerificationTimeoutSeconds` | Optional; default 60, maximum 3600 |

Also set the existing `ReleaseCatalog:Admission:ExpectedSignatureSubject` and
`ExpectedOidcIssuer` to the exact approved producer workflow identity and issuer.
Keep `RegistryClass` and `CatalogLifecycle` under the existing admission policy;
verification does not alter their semantics.

Use the same explicit managed identity in verification and its live acceptance proof.
Grant read access at the governed registry only (for legacy ACR registry permissions,
`AcrPull`), never subscription Contributor or developer-token fallback. No registry
password is required. Keep roles and any temporary proof resources in an ownership ledger.

ACR can redirect blobs to an Azure Storage hostname even with dedicated endpoints
disabled. Configure the exact observed approved host; do not upgrade the registry SKU
or allow `*.blob.core.windows.net` merely to bypass a rejected redirect. Redirects for
OAuth, manifests and referrers are always rejected.

## Pinned dependencies

The Dockerfile checks these digests when downloading the official cosign v3.1.3 release
and commit-pinned trusted root. Runtime validation checks both files before invocation.

| Artifact | SHA-256 |
| --- | --- |
| Linux amd64 cosign 3.1.3 | `4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71` |
| Linux arm64 cosign 3.1.3 | `c5d324e091826b0d7a78eb16fef316450b4eb9aaec045611c08ba06f5e73220a` |
| Trusted root | `6494e21ea73fa7ee769f85f57d5a3e6a08725eae1e38c755fc3517c9e6bc0b66` |

Binary source: [official cosign v3.1.3 release](https://github.com/sigstore/cosign/releases/tag/v3.1.3).
Root source: [pinned root-signing target](https://github.com/sigstore/root-signing/blob/c9bda74ad2221f938f7d2e0295ca3aad2da710a8/targets/trusted_root.json).

Update binaries and the trust root through a reviewed change: verify upstream provenance,
compare signed release checksums and retained bundles, update content pins and host
configuration together, and rerun real-artifact verification on each supported deployed
architecture. Never add flags disabling transparency or certificate identity verification.
Files should remain owned by the deployment image, not writable by untrusted callers.

## Verification and operational boundaries

Focused tests cover exact payload/subject/evidence binding, cryptographic process limits,
authority validation, redirect/token isolation, transport bounds and startup composition.
Real producer signature verification has also been exercised using the packaged Linux
amd64 tool with an empty environment and networking disabled. These checks alone do not
establish live production API admission.

Before accepting production delivery, prove the real release through the administrator
endpoint with the configured managed identity, retrieve its persisted safe identities,
and prove negative cases make no catalog writes. Keep raw payloads, certificates, signer
identities, tool output, bearer tokens and SAS queries out of diagnostic/evidence exports.
Never report an unavailable verifier as a successful or advisory-only admission.

Design rationale: [ADR-0015](adr/0015-production-release-manifest-verification.md).
