# Research: Elsa Package Manifest Generator

## Decision: Use an MSBuild task for MVP generation

Rationale: The required output is an external artifact that must exist before
NuGet pack finalizes package contents. An MSBuild task can run after compilation,
inspect the compiled assembly and XML documentation, receive project/NuGet
metadata as task parameters, write `obj/{configuration}/{targetframework}/elsa-package.json`,
and add the generated file to the package root.

Alternatives considered:

- Roslyn source generator: useful for authoring diagnostics later, but source
  generators are not a natural fit for writing and packing external artifacts.
- Analyzer-only package: good for feedback, insufficient for generation and pack
  inclusion.
- Hybrid from day one: more moving parts than the MVP needs.

## Decision: Ground feature discovery in CShells contracts

Rationale: CShells already defines the runtime feature contract through
`CShells.Features.IShellFeature` and feature metadata through
`CShells.Features.ShellFeatureAttribute`. The generator should inspect those
contracts directly instead of introducing an Elsa-specific feature attribute.
This keeps generated manifests aligned with the code CShells actually activates
and avoids a second source of feature identity.

Alternatives considered:

- Generator-owned `ElsaFeatureAttribute`: rejected because it duplicates
  CShells feature identity and can drift from runtime behavior.
- Configurable base/interface discovery as the primary mechanism: more generic
  than needed now that the CShells contract is known.
- Runtime feature instantiation: explicitly unsafe and unnecessary.

## Decision: Keep manifest-only hints source-only and small

Rationale: Some manifest fields, such as UI hints or secret/sensitive flags, are
catalog concerns rather than CShells runtime concerns. Optional source-only
hints in `Elsa.Platform.PackageManifest.Generator.Hints` can cover these narrow cases
without adding runtime dependencies or replacing `Elsa.Platform.PackageManifests`.

Alternatives considered:

- Put hints in `Elsa.Platform.PackageManifests`: blurs generator input metadata with
  manifest DTOs.
- Add a separate abstractions package: clean, but adds a second package to
  manage for the MVP.
- Broad feature and compatibility hint attributes: too much surface for the MVP;
  rich metadata belongs in `elsa-package.overrides.json`.

## Decision: Use metadata-only inspection for assemblies

Rationale: The constitution and spec prohibit package code execution. The
generator should inspect type definitions, inheritance/interface metadata,
custom attributes, property signatures, and nullable metadata using
metadata-only mechanisms such as `System.Reflection.Metadata` and
`MetadataLoadContext` where higher-level reflection shape is useful.

Alternatives considered:

- Normal reflection loading: simpler but risks assembly load side effects and
  breaks the no-execution guarantee.
- Mono.Cecil: capable, but introduces an extra dependency before platform APIs
  have proven insufficient.
- Executing feature instances: explicitly out of scope and unsafe.

## Decision: Standardize on JsonSchema.Net and JSON Schema Draft 2020-12

Rationale: The repository already uses `JsonSchema.Net`, version `9.2.0` in
central package management. NuGet describes the package as built on
`System.Text.Json`, compatible with modern .NET targets, and current as of April
2026. Draft 2020-12 is the current modern JSON Schema dialect suitable for
versioned manifest schemas and setting schema fragments.

Alternatives considered:

- NJsonSchema: mature, but its package description highlights Json.NET and
  reflection/code-generation features that are heavier than the manifest
  contract needs. Its examples still show draft-04 output, while this ecosystem
  wants a modern contract-first schema.
- Hand-written validation only: insufficient for catalog, runtime validation,
  and external tooling interoperability.
- Multiple schema libraries: unnecessary complexity and inconsistent behavior.

References:

- [JsonSchema.Net on NuGet](https://www.nuget.org/packages/JsonSchema.Net)
- [NJsonSchema on NuGet](https://www.nuget.org/packages/NJsonSchema)

## Decision: Build setting schemas manually for supported MVP shapes

Rationale: The MVP intentionally supports primitive, enum, nullable,
array/list, and dictionary settings and defers complex object settings. Manual
schema construction keeps behavior deterministic, avoids recursive reflection,
and makes unsupported settings easy to diagnose and omit without treating them
as build-blocking manifest entries.

Alternatives considered:

- Generate schemas from arbitrary CLR types: conflicts with the small-MVP
  clarification and risks surprising recursion/cycle behavior.
- Use NJsonSchema-style type generation: adds broad reflection behavior and
  code-generation features not required by the manifest contract.
- Require override files for every setting schema: burdens package authors and
  weakens automatic generation.

## Decision: Merge metadata in four ordered layers

Rationale: The spec defines a clear merge order: inferred metadata, XML
documentation, CShells metadata and manifest hints, then override file. This
preserves useful automation while giving authors explicit escape hatches for
metadata that cannot be inferred. Scalar override behavior and keyed collection
merge behavior are testable and deterministic.

Alternatives considered:

- Override file only: too much manual work.
- Attributes override everything including identity: risks drift from NuGet
  package identity and catalog ambiguity.
- Convention-only inference: too magical for compatibility and documentation
  metadata.

## Decision: Treat multi-targeting differences conservatively

Rationale: NuGet packages should carry one canonical root `elsa-package.json` by
default. Generating per-target intermediate manifests allows comparison, but
feature/setting surface differences should warn or fail unless explicitly
allowed. Silent merging would produce a misleading package-level manifest.

Alternatives considered:

- Emit one manifest per target framework: complicates catalog ingestion and
  runtime tooling.
- Always merge target surfaces: hides differences and can produce invalid
  runtime assumptions.
- Disable multi-targeting support: too restrictive for real package projects.

## Decision: Use root `elsa-package.json` as package path

Rationale: A root manifest is easiest for catalog ingestion, Docker tooling, and
future validators to find. It describes the package itself rather than build or
content assets.

Alternatives considered:

- `build/elsa-package.json`: implies a build asset instead of package metadata.
- `content/elsa/elsa-package.json`: implies installable content and adds path
  complexity.
- Configurable-only path: weakens ecosystem convention.
