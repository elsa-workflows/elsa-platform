# Implementation Plan: Runtime Image Metadata API

**Branch**: `010-runtime-image-metadata-api` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-runtime-image-metadata-api/spec.md`

## Summary

Move deployment-affecting Elsa Docker runtime image metadata into the Catalog API backend so builder catalog, bundle generation, and later planner flows use a single platform-owned source of truth. The first implementation uses strongly typed source-controlled/configured seed metadata for the known professional server, Studio, and combined images. The PRD now also captures the follow-on requirement that runtime image definitions and exposed image-level configurable attributes become backend-configurable without console code changes; database/admin image management and automated registry discovery remain separate product decisions.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core; existing TypeScript/React console remains out of scope.

**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json, options/configuration binding, existing builder catalog and bundle generation services, xUnit, FluentAssertions.

**Storage**: Source-controlled or appsettings-backed runtime image seed metadata in the first slice. Current implementation may use static in-memory backend seed definitions as an intermediate step. No database migration is required until product decisions are made for operator-managed image records, organization/workspace scoping, lifecycle states, and registry tag discovery.

**Testing**: xUnit and FluentAssertions for runtime image catalog validation; ASP.NET Core WebApplicationFactory tests for builder catalog image metadata and bundle image lookup behavior.

**Target platform**: Existing ASP.NET Core Catalog API modular monolith.

**Project Type**: Modular monolith web service with builder APIs and core metadata services.

**Performance Goals**: Runtime image catalog reads are in-memory and complete within normal builder catalog response budgets.

**Constraints**: Deployment-affecting image metadata must not remain authoritative only in Lovable or console source code. Marketing-only fields may remain frontend-owned. Automated Docker registry tag discovery and admin-managed image CRUD are deferred until catalog ownership and scoping are clarified.

**Scale/Scope**: Three initial runtime images: professional server, professional Studio, and professional combined runtime.

## Constitution Check

- **Manifest-first**: Pass. Image metadata is separate from package manifests and does not infer package behavior.
- **No arbitrary code execution**: Pass. No package code or Docker images are executed.
- **Stable contracts**: Pass. Adds builder-facing image DTOs without changing `ValenceControl.PackageManifests`.
- **Schema evolution**: Pass. Image DTO evolution is feature-contract scoped; manifest schemas unchanged.
- **Immutable versions**: Pass. Package version handling unchanged.
- **Approval separation**: Pass. Image metadata does not change package approval/listing.
- **Explicit sources**: Pass. Package sources unchanged.
- **Safe public API**: Pass. Builder image metadata exposes curated, non-secret deployment metadata only.
- **Debuggability**: Pass. Image validation reports incomplete or inconsistent seed metadata.
- **Modular monolith**: Pass. No new service.
- **Runtime Builder readiness**: Pass. Image selection and deployment metadata become backend-owned.
- **Simplicity**: Pass. Seeded strongly typed metadata first; admin/database management deferred.

## Project Structure

### Documentation (this feature)

```text
specs/010-runtime-image-metadata-api/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-images-api.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ValenceControl.PackageCatalog.Core/
│   └── Builder/
│       ├── RuntimeImageCatalog.cs
│       ├── RuntimeImageModels.cs
│       └── RuntimeImageValidator.cs
└── ValenceControl.Api/
    └── Public/Builder/
        ├── BuilderContracts.cs
        └── BuilderEndpoints.cs

tests/
├── ValenceControl.PackageCatalog.Core.Tests/
│   └── RuntimeImageCatalogTests.cs
└── ValenceControl.Api.Tests/
    └── RuntimeImageApiTests.cs
```

**Structure Decision**: Keep runtime image metadata in the existing builder/core area because it directly feeds builder catalog and bundle generation. Avoid persistence projects until product needs admin-managed image records. The frontend should continue to consume image definitions and image-level configurable attributes through builder catalog metadata instead of owning per-image option lists or deployment defaults.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
