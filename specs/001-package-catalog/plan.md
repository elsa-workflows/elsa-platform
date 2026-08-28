# Implementation Plan: Elsa Control Package Catalog

**Branch**: `001-package-catalog` | **Date**: 2026-05-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-package-catalog/spec.md`

## Summary

Build Elsa Control Package Catalog as an ASP.NET Core modular monolith plus a shared
`Elsa.Specifications.PackageManifests` contract package. The catalog indexes explicitly
configured NuGet feeds, extracts versioned `elsa-package.json` manifests without
loading package assemblies, validates and stores manifests with immutable
package-version records, separates validation from approval and listing, and
exposes safe public discovery APIs plus protected admin APIs for source
management, sync, validation diagnostics, and approval.

The implementation starts small: .NET 10 LTS, REST APIs, SQLite, Entity
Framework Core, a hosted background worker for scheduled sync, NuGet client APIs
for feed/package inspection, JSON Schema validation for manifests, and durable
sync/validation/approval diagnostics. The domain model stays database-provider
neutral so PostgreSQL can be introduced later without changing domain concepts.

## Technical Context

**Language/Version**: C# on .NET 10 LTS, supported until November 14, 2028.

**Primary Dependencies**: ASP.NET Core, Entity Framework Core, SQLite provider, NuGet.Protocol, System.Text.Json, .NET JSON Schema validation package, OpenAPI tooling, xUnit and its built-in assertions, Microsoft.AspNetCore.Mvc.Testing.

**Storage**: SQLite for initial durable storage, with EF Core mappings and domain model designed for later PostgreSQL support.

**Testing**: `dotnet test` with unit tests, contract tests, integration tests
using ASP.NET Core test host, SQLite-backed persistence tests, and controlled
NuGet package/archive fixtures.

**Target platform**: Cross-platform server application intended for container or
service hosting. Local development runs on macOS/Linux/Windows with the .NET SDK.

**Project Type**: Web service plus shared library package in one solution.

**Performance Goals**: Public catalog reads should be cacheable and return
quickly for typical builder UI queries. Sync favors correctness and
debuggability over throughput. A controlled feed with at least 20 matching
package versions must sync without one failed item stopping the run.

**Constraints**: No package assembly loading or execution; explicit package
source configuration only; public APIs hide invalid, unapproved, rejected,
suspicious, and unlisted versions; all timestamps stored as UTC; version records
are immutable; durable diagnostics are required for sync and validation.

**Scale/Scope**: First version targets a curated professional package catalog,
not broad NuGet ecosystem crawling. Runtime Builder UI, manifest generation,
full dependency resolution, Sigil license validation, package installation, and
deployment bundle generation remain out of scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Manifest-first**: PASS. Package metadata flows through explicit
  `elsa-package.json` manifests and the `Elsa.Specifications.PackageManifests` contract.
- **No arbitrary code execution**: PASS. NuGet packages are handled as archives;
  only package files, nuspec metadata, and manifest JSON are inspected.
- **Stable contracts**: PASS. `Elsa.Specifications.PackageManifests` is a dependency-light wire
  contract package separate from catalog persistence and runtime internals.
- **Schema evolution**: PASS. Versioned JSON Schema resources, extension metadata
  preservation, unsupported-version validation failures, and breaking-change
  version rules are part of the design.
- **Immutable versions**: PASS. Existing package ID/version records are not
  overwritten when manifest hashes differ; suspicious changes are recorded.
- **Approval separation**: PASS. Validation status, package approval,
  version approval, and listing are distinct states.
- **Explicit sources**: PASS. NuGet sources are configured with include/exclude
  patterns; broad crawling is prohibited.
- **Safe public API**: PASS. Public read APIs filter to valid, approved, listed
  versions only.
- **Debuggability**: PASS. Sync runs, sync items, validation results, approval
  records, and suspicious changes are persisted and exposed to admins.
- **Modular monolith**: PASS. The solution uses separate projects within a
  single ASP.NET Core application boundary and avoids distributed infrastructure.
- **Runtime Builder readiness**: PASS. Contracts and APIs expose packages,
  versions, features, settings schemas, and compatibility checks.
- **Simplicity**: PASS. SQLite, EF Core, hosted worker, REST, and NuGet client
  APIs are sufficient for current requirements. No speculative distributed
  components are introduced.

Post-design re-check: PASS. Phase 1 artifacts preserve the same constraints and
introduce no unjustified constitution violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-package-catalog/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Specifications.PackageManifests/
│   ├── Compatibility/
│   ├── Documentation/
│   ├── Licensing/
│   ├── Schemas/
│   └── Validation/
├── ElsaControl.PackageCatalog.Core/
│   ├── Approvals/
│   ├── Compatibility/
│   ├── Manifests/
│   ├── Packages/
│   ├── Sources/
│   ├── Sync/
│   └── Validation/
├── ElsaControl.Api/
│   ├── Admin/
│   ├── Public/
│   ├── Authentication/
│   └── Program.cs
├── ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/
│   ├── Migrations/
│   ├── Models/
│   └── CatalogDbContext.cs
└── ElsaControl.PackageCatalog.Sources.NuGet/
    ├── PackageArchiveManifestReader.cs
    ├── NuGetPackageSourceClient.cs
    └── NuGetSyncPackageDownloader.cs

tests/
├── Elsa.Specifications.PackageManifests.Tests/
├── ElsaControl.PackageCatalog.Core.Tests/
├── ElsaControl.Api.Tests/
├── ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/
├── ElsaControl.PackageCatalog.Sources.NuGet.Tests/
└── ElsaControl.PackageCatalog.Testing/
```

**Structure Decision**: Use an onion-style modular monolith. `ElsaControl.PackageCatalog.Core`
is the inner catalog model and workflow layer. `ElsaControl.Api` is the delivery
edge. `ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore` and
`ElsaControl.PackageCatalog.Sources.NuGet` are outer adapters. The shared manifest package
remains independent.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
