# ADR-0012: Durable Artifact Storage and Provenance

## Status

Proposed

## Date

2026-08-29

## Context

Elsa Control already validates artifact envelopes/digests and stores safe metadata, but the current upload provider uses local filesystem ZIPs. A production multi-instance control plane needs durable tenant-confined object storage, retention/garbage collection, integrity, backup/restore relationships and supply-chain provenance. Private/custom-code workflows add derived images, signing/attestation and immutable input requirements.

## Proposed Decision

- Store artifact bytes in a durable object/blob provider addressed by opaque provider references; relational tables store safe metadata, digest, type/schema, ownership and lifecycle only.
- Enforce organization/workspace confinement in the storage adapter; never accept arbitrary rooted/local paths as a production trust boundary.
- Use content digest for integrity and deduplication where safe, without weakening ownership/access checks.
- Define retention, legal hold, deletion and backup consistency with desired-state/deployment references.
- Record provenance for producer, inputs, package/image digests and validation; introduce signing/attestation when commercial release policy is defined.
- Raw workflow/package payloads remain out of command/history/audit records.

## Evidence Required Before Acceptance

- Storage/tenant threat model and authorization tests.
- Multi-instance upload/download and lifecycle proof.
- Restore consistency and deletion/retention policy.
- Cost/scale evidence and commercial image/custom-code provenance mapping.

## Consequences if Accepted

- Add a production object-storage adapter and migrate local-only payload references.
- Provider/runtime consumers continue verifying digest before apply.
- Backup and portability designs can reference a clear artifact consistency boundary.
