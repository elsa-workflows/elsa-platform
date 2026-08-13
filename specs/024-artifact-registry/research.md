# Research: Deployment Artifact Registry

## Decision: Store artifact registry metadata in the catalog workspace database

**Rationale**: Workspace ownership, deployment records, permissions, and cockpit metadata already live in the catalog EF database. Artifact registry records need the same workspace authorization and resource isolation boundary, so colocating metadata keeps the first hosted slice simple and consistent. `specs/031-organization-tenancy` adds Organization as the root customer tenant above this workspace boundary.

**Alternatives considered**:

- New artifact database: rejected because it adds operational complexity before object storage or OCI transport exists.
- Store artifact records only in deployment files: rejected because the console and workspace APIs need durable, queryable metadata.

## Decision: Store references and safe summaries only

**Rationale**: The PRD and constitution require deployment artifacts to avoid raw secrets. Storing payloads, manifest JSON, or workflow definitions in catalog tables would expand database risk and duplicate the artifact package. The registry should know what an artifact is and where it can be inspected, not own its bytes.

**Alternatives considered**:

- Store ZIP bytes in the database: rejected because it violates metadata-only scope and creates large-row/backup concerns.
- Store manifest snapshots for convenience: rejected because manifest content can contain sensitive control-plane detail and is already part of the artifact payload.

## Decision: Reuse Deployment.Artifacts contracts for metadata and checksum semantics

**Rationale**: `ValenceControl.Deployment.Artifacts` already defines layout version, artifact identity, content digest, manifest metadata, resource summaries, entries, checksums, and diagnostics. The registry should project these concepts rather than invent a parallel artifact language.

**Alternatives considered**:

- Create independent API-only artifact shapes: rejected because it would drift from the artifact package contract.
- Reference `Deployment.Artifacts` directly from persistence entities: rejected because EF entities should persist primitive values and serialized summaries, not leak IO package internals.

## Decision: Keep inspection refresh explicit and bounded to local/test references first

**Rationale**: Refreshing inspection is useful before validation and dry-run, but cloud/object storage, OCI, signing, and GitOps transports are deferred. A local/test reference adapter provides useful automated coverage while leaving provider transports behind explicit future adapters.

**Alternatives considered**:

- No refresh operation: rejected because stale or tampered references would be invisible until a later feature.
- Implement object storage/OCI now: deferred because it crosses provider and supply-chain boundaries outside this slice.

## Decision: Enable the Artifacts console navigation with live workspace data

**Rationale**: The console already reserves Artifacts as a primary deployment destination and the overview mentions artifacts. Replacing the placeholder with live empty/list/detail/register/refresh states closes a visible UX gap while preserving the actual deployment loop order.

**Alternatives considered**:

- Keep Artifacts disabled until apply exists: rejected because artifact inspection is valuable before apply.
- Fold artifacts into Deployments tab: rejected because artifacts are a reusable input across environments and future validation/dry-run flows.
