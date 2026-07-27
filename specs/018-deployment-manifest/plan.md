# Implementation Plan: Deployment Manifest Parsing

**Branch**: `018-deployment-manifest` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/018-deployment-manifest/spec.md`

## Summary

Add `ValenceControl.Deployment.Manifest` as the next Phase 1 deployment package. The package parses `valence-control/v1alpha1` `EnvironmentManifest` documents from YAML and JSON text, validates the supported shape, and normalizes workflow, variable, feature, package, and recipe entries into `DeploymentResource` values from `ValenceControl.Deployment.Abstractions`. This slice stops before artifact IO, deployment planning, dry-run, apply, CLI, API, runtime adapters, overlays, and secret handling.

## Technical Context

**Language/Version**: C# on .NET 10 using repository-wide `Directory.Build.props`.

**Primary Dependencies**: `ValenceControl.Deployment.Abstractions`, `System.Text.Json`, YamlDotNet for YAML parsing, xUnit and its built-in assertions for tests.

**Storage**: N/A. Manifest parsing is in-memory only.

**Testing**: `dotnet test` for `tests/ValenceControl.Deployment.Manifest.Tests/` plus full solution verification.

**Target platform**: Cross-platform .NET library consumed by future artifact, engine, CLI, API, and operator packages.

**Project Type**: Multi-project .NET repository; this slice adds one source library and one test project.

**Performance Goals**: Deterministic normalization and hashing for normal CI/CD manifest sizes; no throughput target until artifact and engine slices.

**Constraints**: Depend on Deployment Abstractions only plus parser libraries. Do not reference engine, CLI, API, Package Catalog implementation, Runtime Builder implementation, hosting, persistence, migration, UI, or runtime-state packages.

**Scale/Scope**: v1alpha single-manifest parsing and normalization for Phase 1 resources/descriptors.

## Constitution Check

- **Control Plane First**: Pass. Manifest resources describe control-plane desired state only.
- **Bounded Subsystems**: Pass. Manifest depends on deployment abstractions and not on engine/API/catalog/runtime implementation packages.
- **Contract Stability**: Pass. v1alpha version is explicit and unsupported versions produce diagnostics.
- **Safety By Design**: Pass. Manifest metadata excludes raw secret handling; secret references are deferred.
- **Incremental Verifiability**: Pass. Tests can verify parsing/normalization independently from artifacts or engine execution.

## Project Structure

### Documentation (this feature)

```text
specs/018-deployment-manifest/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── manifest-shape.md
│   └── dependency-boundaries.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
  ValenceControl.Deployment.Manifest/
    EnvironmentManifest.cs
    ManifestMetadata.cs
    ManifestResourceEntries.cs
    ManifestParseResult.cs
    ManifestReader.cs
    ManifestNormalizer.cs
    ManifestResourceMapperRegistry.cs
    IManifestReader.cs
    IManifestNormalizer.cs
    IManifestResourceMapper.cs

tests/
  ValenceControl.Deployment.Manifest.Tests/
    ManifestReaderTests.cs
    ManifestNormalizationTests.cs
    ManifestDiagnosticTests.cs
    ManifestExtensionTests.cs
    ManifestBoundaryTests.cs
```

**Structure Decision**: Keep parsing and normalization in `ValenceControl.Deployment.Manifest`. Artifact layout/checksum behavior belongs in `ValenceControl.Deployment.Artifacts`, and reconciliation belongs in `ValenceControl.Deployment.Engine`.

## Phase Plan

### Phase 1: Planning And Contracts

Outcome:

- Spec Kit artifacts define manifest shape, boundaries, and tasks.

Exit gate:

- `/speckit-analyze` finds no critical inconsistencies.

### Phase 2: Project Skeleton

Outcome:

- Source/test projects exist and are added to the solution.
- Package dependency on deployment abstractions is in place.

Exit gate:

- Empty project skeleton builds.

### Phase 3: Manifest Model And Readers

Outcome:

- Environment manifest records and YAML/JSON reader parse supported manifest text.
- Parse errors return diagnostics instead of leaking parser exceptions.

Exit gate:

- Valid/invalid YAML and JSON reader tests pass.

### Phase 4: Normalization And Resource Mapping

Outcome:

- Built-in resource mappers normalize workflows, variables, features, packages, and recipes.
- Extension mapper registry supports custom resource sections.
- Deterministic desired-state hashes are produced.

Exit gate:

- Normalization, duplicate identity, path validation, unknown section, and extension tests pass.

### Phase 5: Boundaries And Verification

Outcome:

- Boundary tests enforce allowed dependencies.
- Docs/quickstart reflect verification.

Exit gate:

- Focused tests, full solution tests, and `git diff --check` pass.

## Deferred Work

- Artifact folder/ZIP IO and checksum manifests.
- Deployment planner, validation orchestration, diff, dry-run, apply, and history persistence.
- CLI and API surfaces.
- Workflow/variable runtime adapters.
- Package catalog validation implementation.
- Overlays, secret references, signatures, OCI, GitOps, operators, Kubernetes CRDs, policy engines, and multi-tenant reconciliation.

## Complexity Tracking

No constitution violations are introduced.
