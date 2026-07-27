# Implementation Plan: Generator Adoption Fixes for Elsa Shell Modules

**Branch**: `codex/003-generator-adoption-fixes` | **Date**: 2026-05-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-generator-adoption-fixes/spec.md`

## Summary

Harden `ValenceControl.PackageManifest.Generator` for broad Elsa Core module adoption by
fixing three build-package behaviors discovered during preview rollout: warning
severity must not make the MSBuild task return failure when only warnings are
logged, delegate-shaped shell-feature properties must be treated as
non-configurable code hooks before setting schema validation, unsupported
non-delegate CLR-only setting properties must be omitted with low-importance
diagnostics instead of failing normal builds, and multi-targeted package
projects must include exactly one root `elsa-package.json` without consumer-side
`TargetsForTfmSpecificContentInPackage` workarounds. A follow-up pack hardening
fix ensures build-then-pack pipelines using `dotnet pack --no-build` reuse the
manifest generated during build instead of rerunning metadata inspection with
incomplete pack-time reference paths.

The implementation stays inside the existing generator projects. It refines
diagnostic classification, setting discovery/schema filtering, and MSBuild pack
targets while adding focused unit and integration tests around the adoption
cases.

## Technical Context

**Language/Version**: C# on .NET 10 LTS with nullable reference types and
deterministic builds.

**Primary Dependencies**: Existing MSBuild task APIs, System.Reflection metadata
inspection, System.Text.Json, ValenceControl.PackageManifests validation, xUnit,
xUnit's built-in assertions, and existing generator test helpers.

**Storage**: File artifacts only. Inputs are compiled assemblies, XML docs,
project/NuGet metadata, references, and optional override files. Outputs are
intermediate manifests and NuGet package entries.

**Testing**: `dotnet test` with core unit tests, MSBuild task policy tests, and
integration/package inspection tests using sample projects.

**Target platform**: Cross-platform .NET SDK builds on macOS, Linux, Windows,
and CI. Consumers are Elsa shell-feature class library package projects.

**Project Type**: Build-time NuGet package and MSBuild task hardening inside the
existing generator feature.

**Performance Goals**: No measurable regression from the current generator; the
additional type-shape filtering should remain metadata-only and add less than
100 ms for representative modules with fewer than 50 features and 500 public
properties.

**Constraints**: No package code execution; no feature constructors or property
getters; no manifest schema redesign; no new public runtime dependency for
consumers; preserve one private package reference workflow; preserve root
manifest path by default.

**Scale/Scope**: Covers existing Elsa Core shell-feature modules with direct
delegate hooks, delegate-valued collections/dictionaries, unsupported CLR-only
property shapes such as `System.Type`, warning-severity rollout, and
multi-target pack inclusion. Complex object schema support remains out of scope;
unsupported properties are omitted until explicitly supported by the manifest
contract.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Manifest-first**: PASS. The feature preserves `elsa-package.json` as the
  distribution contract and improves its generation/inclusion.
- **No arbitrary code execution**: PASS. Delegate filtering and setting
  discovery remain metadata-only; no constructors, getters, factories, or
  callbacks are invoked.
- **Stable contracts**: PASS. No `ValenceControl.PackageManifests` wire-contract change is
  planned.
- **Schema evolution**: PASS. No schema version change is needed because the
  feature excludes non-configurable code hooks rather than adding new manifest
  fields.
- **Immutable versions**: N/A. This feature affects generation before package
  publication, not catalog indexing of immutable versions.
- **Approval separation**: N/A. Approval/listing state is not changed.
- **Explicit sources**: N/A. Package source scanning is not changed.
- **Safe public API**: N/A. No catalog API behavior changes.
- **Debuggability**: PASS. Diagnostic policy becomes more consistent, and
  omitted non-manifestable properties remain traceable through low-importance
  diagnostics without noisy default warnings.
- **Modular monolith**: PASS. No distributed infrastructure or service changes.
- **Runtime Builder readiness**: PASS. Deploy-time settings become cleaner by
  excluding code-only hooks and unsupported CLR-only shapes that Runtime Builder
  cannot configure.
- **Simplicity**: PASS. Uses existing generator projects, models, targets, and
  test helpers; no new dependency or broad abstraction is required.

Post-design re-check: PASS. Phase 1 artifacts keep the change bounded to
existing build-package contracts, metadata-only inspection, deterministic pack
inclusion, and targeted tests.

## Project Structure

### Documentation (this feature)

```text
specs/003-generator-adoption-fixes/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── diagnostic-policy.md
│   ├── setting-discovery.md
│   └── package-inclusion.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ValenceControl.PackageManifest.Generator/
│   └── build/
│       ├── ValenceControl.PackageManifest.Generator.props
│       └── ValenceControl.PackageManifest.Generator.targets
├── ValenceControl.PackageManifest.Generator.Core/
│   ├── AssemblyInspection/
│   ├── Generation/
│   │   ├── ManifestGenerator.cs
│   │   ├── SettingDiscoveryService.cs
│   │   └── MultiTargetManifestCoordinator.cs
│   ├── SchemaGeneration/
│   │   └── SettingSchemaGenerator.cs
│   └── Validation/
│       ├── GenerationDiagnostics.cs
│       └── ValidationSeverityPolicy.cs
└── ValenceControl.PackageManifest.Generator.MSBuild/
    ├── GenerateElsaPackageManifestTask.cs
    └── Packaging/

tests/
├── ValenceControl.PackageManifest.Generator.Core.Tests/
├── ValenceControl.PackageManifest.Generator.MSBuild.Tests/
├── ValenceControl.PackageManifest.Generator.IntegrationTests/
└── ValenceControl.PackageManifest.Generator.Testing/
```

**Structure Decision**: Extend the existing generator core/MSBuild/package
facade layout from `002-package-manifest-generator`. Core owns metadata-only
setting classification and severity policy; MSBuild owns task return behavior;
the package facade targets own pack item inclusion. Tests use existing unit,
MSBuild, integration, sample-project, and package-inspection projects.

## Complexity Tracking

No constitution violations. No new projects, infrastructure, schema versions, or
third-party dependencies are planned.
