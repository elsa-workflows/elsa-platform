# Implementation Plan: Elsa Package Manifest Generator

**Branch**: `002-package-manifest-generator` | **Date**: 2026-05-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-package-manifest-generator/spec.md`

## Summary

Build `Elsa.Platform.PackageManifest.Generator` as a NuGet build package for Elsa
professional extension libraries. The package adds MSBuild targets and a safe
metadata-inspection task that runs after compilation and before pack, generates
one deterministic `elsa-package.json`, validates it with `Elsa.Platform.PackageManifests`,
and includes it at the NuGet package root.

The implementation stays deliberately small: CShells metadata discovery, a tiny
manifest-hint surface where CShells has no concept for a field, an MSBuild task,
a focused generator core, manual schema construction for the supported setting
shapes, XML documentation parsing, JSON override merging, and packaging tests
against sample projects. Analyzer/source-generator authoring support remains
deferred.

## Technical Context

**Language/Version**: C# on .NET 10 LTS with nullable reference types and
deterministic builds.

**Primary Dependencies**: MSBuild task APIs, System.Reflection.Metadata,
MetadataLoadContext where useful for metadata-only inspection, System.Xml.Linq,
System.Text.Json, JsonSchema.Net, Elsa.Platform.PackageManifests, NuGet.Versioning,
xUnit, FluentAssertions.

**Storage**: File artifacts only. Inputs are compiled assemblies, XML
documentation files, project/NuGet metadata, referenced assembly metadata, and
optional override JSON. Outputs are intermediate `elsa-package.json` files and
NuGet package contents.

**Testing**: `dotnet test` with unit tests for generator core behavior,
MSBuild/pack integration tests using sample projects, package inspection tests,
determinism tests, and safety tests proving constructors/property getters are
not invoked.

**Target Platform**: Cross-platform .NET SDK builds on macOS, Linux, Windows,
and CI. Consuming projects are class library NuGet package projects.

**Project Type**: Build-time NuGet package plus optional source-only manifest
hints and shared generator libraries in the existing solution.

**Performance Goals**: Generation should add no more than 2 seconds for typical
package projects with fewer than 50 feature types and 500 settings on warm
builds. Generated manifests must stay under 1 MB; override files must stay under
256 KB.

**Constraints**: No package code execution; no feature constructors or property
getters; one package author reference; root `elsa-package.json`; one canonical
manifest for multi-targeted packages; complex object settings deferred; manifest
hints must not become the manifest contract.

**Scale/Scope**: MVP supports primitives, enums, nullable values, arrays/lists,
dictionaries, common DataAnnotations validation attributes, XML documentation,
CShells `IShellFeature`/`ShellFeatureAttribute` metadata, optional source-only
manifest hints, override files, schema validation, and predictable
multi-targeting.
Recursive object schema generation and Roslyn analyzers are out of scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Manifest-first**: PASS. The generator emits the explicit
  `elsa-package.json` distribution contract and uses `Elsa.Platform.PackageManifests`.
- **No arbitrary code execution**: PASS. Discovery uses metadata inspection of
  assemblies, XML docs, and JSON files only; constructors/getters are forbidden.
- **Stable contracts**: PASS. Generated JSON is based on
  `Elsa.Platform.PackageManifests`; CShells metadata and manifest hints are generator
  inputs only.
- **Schema evolution**: PASS. The plan standardizes on versioned manifest schema
  validation and Draft 2020-12 JSON Schema resources owned by
  `Elsa.Platform.PackageManifests`.
- **Immutable versions**: N/A. This feature generates package artifacts and does
  not index package versions.
- **Approval separation**: N/A. Approval is catalog behavior, not generator
  behavior.
- **Explicit sources**: N/A. The generator processes the current project build,
  not package feeds.
- **Safe public API**: N/A. This feature exposes build/package contracts, not a
  public web API.
- **Debuggability**: PASS. Build diagnostics include generated path, package
  inclusion, feature count, validation errors, unsupported types, and override
  issues.
- **Modular monolith**: PASS. Adds projects to the existing solution without
  distributed infrastructure.
- **Runtime Builder readiness**: PASS. The generated manifest includes feature,
  setting, schema, compatibility, documentation, and extension metadata.
- **Simplicity**: PASS. The design uses an MSBuild task and focused libraries;
  analyzers, recursive schemas, and plugin systems are deferred.

Post-design re-check: PASS. Phase 1 artifacts preserve the no-execution,
manifest-first, dependency-light, and small-MVP constraints. No constitution
violations are introduced.

## Project Structure

### Documentation (this feature)

```text
specs/002-package-manifest-generator/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── annotations.md
│   ├── msbuild-contract.md
│   ├── override-file.schema.json
│   └── package-layout.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Platform.PackageManifests/
│   ├── Schemas/
│   └── Validation/
├── Elsa.Platform.PackageManifest.Generator/
│   ├── build/
│   ├── buildTransitive/
│   ├── src/
│   │   └── Elsa.Platform.PackageManifest.Generator.Hints/
│   └── Elsa.Platform.PackageManifest.Generator.csproj
├── Elsa.Platform.PackageManifest.Generator.Core/
│   ├── AssemblyInspection/
│   ├── Documentation/
│   ├── Generation/
│   ├── Overrides/
│   ├── SchemaGeneration/
│   └── Validation/
└── Elsa.Platform.PackageManifest.Generator.MSBuild/
    ├── GenerateElsaPackageManifestTask.cs
    └── Packaging/

tests/
├── Elsa.Platform.PackageManifest.Generator.Core.Tests/
├── Elsa.Platform.PackageManifest.Generator.MSBuild.Tests/
├── Elsa.Platform.PackageManifest.Generator.IntegrationTests/
└── Elsa.Platform.PackageManifest.Generator.Testing/
```

**Structure Decision**: Use a small package facade plus separate core and
MSBuild projects. `Elsa.Platform.PackageManifest.Generator` is the NuGet package authors
reference; it carries targets, props, task binaries, and optional source-only
manifest hints. `Core` owns deterministic generation and validation
orchestration. `MSBuild` owns task binding and pack item integration. The existing
`Elsa.Platform.PackageManifests` package remains the wire contract and schema owner.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Separate Core/MSBuild projects | Keeps pure generation logic testable without MSBuild task harness and keeps task binding thin. | A single task project would mix MSBuild concerns with manifest generation and make unit testing harder. |
