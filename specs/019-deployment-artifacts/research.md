# Research: Deployment Artifact Packaging

## Decision: Start With Folder And ZIP Artifacts

**Rationale**: Folder artifacts are easiest to inspect in CI and tests, while ZIP artifacts prove portable single-file transport without introducing registry semantics. Both formats can share the same logical layout and reader contracts.

**Alternatives considered**:

- OCI artifact first: deferred to Phase 2 because registry auth, media types, signing, and promotion flows would obscure the Phase 1 artifact contract.
- NuGet package first: deferred because deployment artifacts are not package dependencies and should not inherit NuGet-specific lifecycle semantics.
- Folder only: rejected because Phase 1 needs at least one portable archive form for later CLI/API upload flows.

## Decision: Use SHA-256 For Phase 1 Checksums

**Rationale**: SHA-256 is already used by deployment abstractions and manifest desired-state hashes, is broadly available in .NET, and is sufficient for deterministic content identity in Phase 1.

**Alternatives considered**:

- Multiple algorithms in Phase 1: deferred until a consumer requires algorithm negotiation.
- Non-cryptographic hashes: rejected because artifact integrity diagnostics should use a standard digest format.

## Decision: Make Artifact Build Atomic

**Rationale**: A failed build must not leave an output that downstream tools can mistake for valid. The builder should stage output and publish only after metadata, payloads, and checksums are complete.

**Alternatives considered**:

- Best-effort partial artifacts: rejected because later dry-run/apply consumers need clear validity semantics.
- Caller-managed cleanup only: rejected because the package should enforce safe defaults.

## Decision: Keep Artifact Identity Content-Derived And Environment-Neutral

**Rationale**: Artifact identity must survive promotion across environments and should not change because of target names, transport, or hosting model.

**Alternatives considered**:

- Include target/environment in identity: rejected because it would make promotion and GitOps comparison harder.
- Use timestamp identity: rejected because unchanged content would not be stable.

## Decision: Keep Artifact IO Separate From Reconciliation

**Rationale**: Artifact packages should be readable without an engine, target, database, API host, or runtime adapter. This preserves the manifest -> artifact boundary and lets future engine, CLI, and API packages reuse the same contracts.

**Alternatives considered**:

- Combine artifact and engine packages: rejected because it would couple package IO to dry-run/apply behavior before the engine contract has implementation feedback.
- Put artifact IO in CLI: rejected because APIs and operators need the same behavior.
