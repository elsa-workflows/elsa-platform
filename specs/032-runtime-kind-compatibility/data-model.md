# Data Model: Runtime Kind Compatibility

## Runtime Kind

Represents the kind of application host that can consume a package or feature.

Fields:

- `value`: Required machine-readable string identifier.
- `official`: Whether the value is one of the Elsa-owned identifiers.

Validation:

- Must be non-empty after trimming.
- Must be stable and machine-readable.
- Must not contain whitespace.
- Matching is case-insensitive.
- Unknown valid values are preserved.

Official values:

- `elsa.server`: Elsa Server applications, including ASP.NET Core workflow server packages.
- `elsa.studio`: Elsa Studio applications and Studio UI/client packages.

## Package Compatibility Declaration

Represents package-level compatibility metadata.

Fields:

- `runtimeKinds`: Optional list of runtime kind values.
- Existing compatibility metadata remains unchanged.

Validation:

- If present, the list must contain at least one runtime kind.
- Values must pass runtime kind validation.
- Duplicate values are invalid after case-insensitive comparison.

Rules:

- Provides the default compatibility for features that do not declare feature-level runtime kinds.
- If absent on an existing manifest, the effective package runtime compatibility is `elsa.server`.

## Feature Compatibility Declaration

Represents compatibility metadata for one package feature.

Fields:

- `runtimeKinds`: Optional list of runtime kind values.
- Existing feature metadata remains unchanged.

Validation:

- If present, the list must contain at least one runtime kind.
- Values must pass runtime kind validation.
- Duplicate values are invalid after case-insensitive comparison.

Rules:

- Overrides package-level runtime-kind compatibility for that feature.
- If absent, the feature inherits package-level runtime compatibility.
- If both package-level and feature-level declarations are absent, the effective feature runtime compatibility is `elsa.server`.

## Effective Runtime Compatibility

Resolved compatibility used by catalog/API/UI consumers.

Fields:

- `packageRuntimeKinds`: Effective package runtime kinds.
- `featureRuntimeKinds`: Effective runtime kinds for each feature.
- `source`: Whether compatibility came from package declaration, feature declaration, or backward-compatible defaulting.

Rules:

- A package is compatible with a target runtime if its effective package runtime kinds contain the target value, case-insensitively.
- A feature is compatible with a target runtime if its effective feature runtime kinds contain the target value, case-insensitively.
- Unknown valid runtime kinds are not treated as compatible with Elsa Server or Elsa Studio unless their value matches the target runtime.
