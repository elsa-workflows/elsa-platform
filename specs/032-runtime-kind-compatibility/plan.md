# Implementation Plan: Runtime Kind Compatibility

**Branch**: `032-runtime-kind-compatibility` | **Date**: 2026-06-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/032-runtime-kind-compatibility/spec.md`

## Summary

Add runtime-kind compatibility metadata to package manifests and catalog projections so Elsa Server and Elsa Studio package experiences can share one catalog while filtering packages and features by the application host they support. Runtime kinds are open-ended machine-readable string identifiers, with official Elsa Server and Elsa Studio constants, package-level defaults, feature-level overrides, and backward-compatible Elsa Server behavior for existing manifests.

## Technical Context

**Language/Version**: C# on .NET 10 for manifest/catalog/API/runtime-builder code; TypeScript/React for console consumers where filtering or display surfaces are affected.

**Primary Dependencies**: `Elsa.Specifications.PackageManifests`, `ElsaControl.PackageCatalog.Core`, EF Core catalog persistence, ASP.NET Core minimal APIs, existing Runtime Builder catalog services, React Router/TanStack Query console code, xUnit and its built-in assertions, Vitest where console model behavior changes.

**Storage**: Existing catalog relational database through `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore`. Runtime kind declarations are safe metadata stored with package-version and feature compatibility records or equivalent serialized projections; no secrets or executable runtime data are stored.

**Testing**: Focused `dotnet test` for PackageManifests, PackageCatalog.Core, PackageCatalog.Persistence.EntityFrameworkCore, Api, and RuntimeBuilder tests impacted by catalog projections; focused `npm test`/Vitest for console model filtering if TypeScript surfaces change; `git diff --check`.

**Target platform**: Cross-platform Elsa Control API/catalog host and React console package/runtime builder experiences.

**Project Type**: Modular monolith web service plus shared manifest contract package and hosted console.

**Performance Goals**: Runtime-kind filtering must be metadata-only and should not require package code execution or extra per-feature package archive reads after sync. Public catalog queries for typical package lists should remain bounded to the existing query shape.

**Constraints**: Runtime kind is not a closed enum in manifest wire contracts. Manifest ingestion must preserve unknown valid runtime kinds. Existing manifests without runtime-kind declarations remain Elsa Server-compatible only. Runtime kind must stay distinct from runtime capabilities.

**Scale/Scope**: Applies to package manifests, feature manifests, validation, generation/overrides where package authors set metadata, catalog ingestion/projections, public package/feature API contracts, and Runtime Builder filtering for Elsa Server. Future Studio package UX can consume the same metadata but is not implemented unless existing code already has a natural touchpoint.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. The feature adds catalog/manifest compatibility metadata only and does not reconcile runtime workflow state.
- **Bounded Subsystems**: PASS. Manifest contracts own wire shape, catalog owns ingestion/projection, API/console consume projected metadata. Deployment/runtime internals are not coupled to catalog persistence.
- **Contract Stability**: PASS. The manifest schema remains versioned and accepts an additive compatibility field. Backward compatibility is explicit for undeclared existing manifests.
- **Safety By Design**: PASS. Runtime-kind compatibility is metadata-only and does not require loading or executing package assemblies.
- **Incremental Verifiability**: PASS. Manifest validation, catalog ingestion, API projection, and builder filtering can be tested independently.

Post-design re-check: PASS. Design artifacts keep the change additive, metadata-only, dependency-light, and independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/032-runtime-kind-compatibility/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-kind-compatibility.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Specifications.PackageManifests/
│   ├── Compatibility/
│   ├── FeatureManifest.cs
│   ├── Schemas/
│   └── Validation/
├── Elsa.Specifications.PackageManifest.Generator.Core/
│   ├── Generation/
│   └── Overrides/
├── ElsaControl.PackageCatalog.Core/
│   ├── Compatibility/
│   ├── Manifests/
│   └── Packages/
├── ElsaControl.PackageCatalog.Abstractions/
│   └── Catalog/
├── ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/
│   └── Models/
├── ElsaControl.Api/
│   └── Public/
└── ElsaControl.Console/
    └── src/features/

tests/
├── Elsa.Specifications.PackageManifests.Tests/
├── Elsa.Specifications.PackageManifest.Generator.Core.Tests/
├── ElsaControl.PackageCatalog.Core.Tests/
├── ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
├── ElsaControl.Api.Tests/
└── ElsaControl.Console/
```

**Structure Decision**: Extend the existing manifest compatibility contract and catalog ingestion/projection paths in place. Avoid a new subsystem because runtime-kind compatibility is part of package metadata, not deployment or runtime execution.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
