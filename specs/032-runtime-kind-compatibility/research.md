# Research: Runtime Kind Compatibility

## Decision: Runtime kinds are open-ended strings with official constants

**Rationale**: The manifest wire contract needs to support Elsa Server, Elsa Studio, and future/custom application hosts without schema changes. Official constants give first-party code stable values while keeping package author metadata extensible.

**Alternatives considered**:

- Closed enum: rejected because Studio and third-party host kinds would require manifest/schema releases for every new kind.
- Free-form display names: rejected because display text is hard to compare, localize, and preserve as a stable contract.

## Decision: Use package-level defaults and feature-level overrides

**Rationale**: Most packages target one host kind, so package-level compatibility keeps manifests concise. Mixed packages still need feature-level declarations so a single package can expose server and studio features without forcing consumers to treat the whole package as compatible.

**Alternatives considered**:

- Package-level only: rejected because it cannot represent mixed packages.
- Feature-level only: rejected because it repeats the same declaration across every normal server package feature.

## Decision: Existing undeclared manifests default to Elsa Server only

**Rationale**: Existing generated manifests represent server package behavior today. Preserving server compatibility avoids a breaking catalog change, while not defaulting to Studio prevents old server packages from appearing in future Studio package experiences.

**Alternatives considered**:

- Treat undeclared manifests as compatible with every runtime: rejected because it would make Studio compatibility unsafe.
- Treat undeclared manifests as compatible with no runtime: rejected because it would break current Runtime Builder/package catalog behavior.

## Decision: Runtime kind remains separate from runtime capabilities

**Rationale**: Runtime kind answers "which host type can consume this package or feature"; capabilities answer "which behaviors are available inside that compatible host." Keeping the concepts separate avoids overloading capabilities with product identity.

**Alternatives considered**:

- Model host type as a capability: rejected because a package can be host-compatible before capability matching happens, and host identity needs defaulting/inheritance rules.

## Decision: Matching is case-insensitive while canonical values are preserved

**Rationale**: Catalog consumers should not reject otherwise valid packages because of casing differences, but package metadata should preserve the publisher's canonical value for diagnostics and display.

**Alternatives considered**:

- Case-sensitive matching: rejected because it is too fragile for an open string contract.
- Lowercase-only storage: rejected because preserving publisher metadata is useful for diagnostics and round-tripping.
