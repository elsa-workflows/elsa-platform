# Research: Deployment Manifest Parsing

## Decision: Add A Dedicated Manifest Package

**Rationale**: Manifest parsing and normalization are reusable by artifact builders, CLI commands, API endpoints, and the engine. Keeping them in `Elsa.Platform.Deployment.Manifest` prevents the CLI or engine from owning the manifest language.

**Alternatives considered**:

- Put parsing in the engine. Rejected because artifact build and validation need manifest parsing before engine execution exists.
- Put parsing in the CLI. Rejected because API/operator paths must share the same behavior.

## Decision: Support YAML And JSON In The Same Reader Contract

**Rationale**: YAML is the primary human-authored format, while JSON is useful for generated manifests and deterministic test fixtures. Both normalize into the same manifest model and resource hashes.

**Alternatives considered**:

- YAML only. Rejected because JSON is cheap to support through `System.Text.Json` and improves automation.
- JSON only. Rejected because the roadmap calls for Git-friendly authoring and examples are YAML.

## Decision: Use Built-In Mappers For Phase 1 Resource Sections

**Rationale**: Workflow, variable, feature, package, and recipe sections need stable resource type mappings now. Feature, package, and recipe sections remain descriptors and do not imply apply support.

**Alternatives considered**:

- Treat all sections as generic dictionaries. Rejected because resource identity, diagnostics, and duplicate detection need typed rules.
- Fully implement package/feature/recipe apply behavior. Deferred to later deployment slices.

## Decision: Deterministic Hashes Use Canonical JSON

**Rationale**: Desired-state hashes must be stable across YAML and JSON input with equivalent content. Canonical JSON generated from normalized resource payloads gives stable hashes without relying on parser-specific formatting.

**Alternatives considered**:

- Hash raw manifest text. Rejected because equivalent YAML and JSON would not match.
- Defer hashes to artifact IO. Rejected because normalized resources need desired-state hashes for planning.

## Decision: Unknown Resource Sections Are Rejected Unless Mapped

**Rationale**: Silently ignoring unknown sections would make dry-run/apply unsafe. A mapper registry keeps extension possible without accepting accidental typos.

**Alternatives considered**:

- Preserve unknown sections as opaque descriptors. Rejected for Phase 1 safety.
- Hard-code all possible resource sections. Rejected because third-party resources need a future extension path.
