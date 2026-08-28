# Research: Artifact Envelope And Types

## Decision 1: Use a typed envelope as the producer handoff

**Decision**: All producers submit an artifact envelope containing immutable identity, artifact type, schema version, digests, payload reference, producer metadata, safe display metadata, compatibility hints, diagnostics, and audit metadata.

**Rationale**: This keeps Elsa Control producer-neutral while giving runtime integrations enough metadata to determine whether they can apply an artifact. It also avoids a workflow-specific registry model.

**Alternatives Considered**:

- Store raw workflow definitions in Elsa Control. Rejected because Elsa Control should not become the workflow runtime source database or interpret workflow internals.
- Keep only the current generic metadata registry. Rejected because runtime command sync needs artifact type and compatibility semantics.

## Decision 2: Built-in artifact type IDs plus extension registration

**Decision**: Define built-in artifact type IDs starting with `elsa.workflow-definition`, and allow future extension registrations for additional types.

**Rationale**: Runtime appliers need stable type IDs. Built-in types cover first-party Elsa workflows, while extension registration keeps the architecture general purpose.

**Alternatives Considered**:

- Infer type from payload layout. Rejected because Elsa Control should not inspect payload internals to decide applicability.
- Allow arbitrary free-form type IDs. Rejected because typos and unowned semantics would cause unsafe deployment targeting.

## Decision 3: Metadata-only catalog persistence

**Decision**: Catalog tables store envelope metadata, payload references, digests, compatibility hints, and safe diagnostics only.

**Rationale**: This preserves the source-of-truth boundary: Elsa Control owns deployable artifact records and deployment intent, while payload storage can be provided by producer storage, object storage, OCI, or another provider later.

**Alternatives Considered**:

- Store payload JSON in artifact rows. Rejected due payload opacity, data exposure, migration cost, and secret leakage risk.
- Implement object storage upload in this slice. Rejected to keep envelope semantics separate from storage transport.

## Decision 4: Compatibility hints are advisory but structured

**Decision**: Envelope compatibility hints use structured fields for artifact type support, runtime family, version range, required capabilities, and optional environment constraints.

**Rationale**: The platform can preflight obvious mismatches without understanding payload internals. Runtime appliers remain authoritative for final validation.

**Alternatives Considered**:

- Treat compatibility as opaque text. Rejected because deployment planning cannot reliably use it.
- Make Elsa Control understand workflow schema internals. Rejected because that belongs in the workflow runtime applier.

## Decision 5: Backward-compatible registry projection

**Decision**: Existing manual artifact records without explicit envelope fields project as envelope-compatible records with default type and producer metadata.

**Rationale**: The 024 registry slice is already implemented. The envelope upgrade should not break existing console/API behavior or tests.

**Alternatives Considered**:

- Require a destructive migration before reads. Rejected because it increases rollout risk.
- Maintain two separate artifact record models. Rejected because deployment command sync should target one registry concept.
