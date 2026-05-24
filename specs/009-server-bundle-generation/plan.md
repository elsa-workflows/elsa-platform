# Implementation Plan: Server-Side Bundle Generation

**Branch**: `009-server-bundle-generation` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-server-bundle-generation/spec.md`

## Summary

Add a protected Runtime Builder bundle-generation capability to the existing Catalog API modular monolith. The first slice accepts normalized builder intent from trusted frontend/proxy clients using dedicated builder-client credentials, validates package/source/runtime selections using existing catalog visibility and compatibility services, renders the required deployment files server-side as ephemeral response data, and returns structured findings when generation cannot proceed. The backend bundle output becomes a new platform contract; existing browser output is used only for migration comparison fixtures.

## Technical Context

**Language/Version**: C# on .NET 10 LTS for API/Core; existing TypeScript/React console remains out of scope.

**Primary Dependencies**: ASP.NET Core minimal APIs and authorization, dedicated builder-client API-key authentication/authorization, existing workspace identity adapter, existing compatibility checks, System.Text.Json, existing catalog query services, xUnit, FluentAssertions.

**Storage**: No new durable storage for generated files. Existing relational catalog database remains the source for package/source/version visibility. Optional non-secret generation diagnostics are logged or emitted through existing diagnostics patterns only.

**Testing**: xUnit and FluentAssertions for core bundle-generation services; ASP.NET Core WebApplicationFactory integration tests for public/trusted and workspace bundle endpoints; fixture-based migration comparison tests for representative builder states.

**Target Platform**: Existing ASP.NET Core Catalog API deployed as the modular monolith.

**Project Type**: Modular monolith web service with public/trusted builder APIs protected by a dedicated builder-client policy, workspace builder APIs, core domain services, and existing EF Core persistence adapters.

**Performance Goals**: Generate required text bundle files for representative builder requests in under 1 second in local integration tests, excluding any external package indexing. Generation must be CPU/local-data only and must not fetch packages or call external registries.

**Constraints**: Generated files are ephemeral and not retrievable after the response completes. Direct browser calls to bundle generation are protected; anonymous builder users reach generation through trusted frontend/proxy clients using dedicated builder-client credentials rather than broad admin credentials. Blocking errors return structured findings and no files. `Program.Generated.cs` is optional reference output only. Backend output is validated against the new bundle contract rather than exact browser parity.

**Scale/Scope**: First slice supports Docker Compose bundle output with `config.json`, `packages.lock.json`, `docker-compose.yml`, `.env.example`, `README.md`, and optional `Program.Generated.cs`. Saved configurations, server-side planning, stored ZIP downloads, signed locks, live deployment, Kubernetes/Helm/Azure templates, and managed hosting are deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Manifest-first**: Pass. Bundle inputs and selected package metadata use indexed manifest data and existing catalog query services.
- **No arbitrary code execution**: Pass. Bundle generation renders text artifacts from request data and stored manifests; it does not load or execute package assemblies.
- **Stable contracts**: Pass. The feature adds builder bundle API contracts without changing `Elsa.Platform.PackageManifests`.
- **Schema evolution**: Pass. No manifest schema changes are required; bundle DTO evolution is documented in this feature contract.
- **Immutable versions**: Pass. Selected package versions are read from existing immutable catalog records; no package content mutation occurs.
- **Approval separation**: Pass. Existing valid/approved/listed visibility and compatibility checks remain separate from bundle rendering.
- **Explicit sources**: Pass. Package selections remain source-qualified and package sources are explicitly supplied or resolved from visible catalog sources.
- **Safe public API**: Pass. Direct bundle generation is protected from untrusted browser callers through a dedicated builder-client policy; public package visibility remains valid/approved/listed.
- **Debuggability**: Pass. Bundle findings and non-secret generation diagnostics are explicit and inspectable through logs/tests.
- **Modular monolith**: Pass. The design stays within existing API/Core projects and does not add services.
- **Runtime Builder readiness**: Pass. The feature makes Runtime Builder deployment output backend-owned and reusable by Lovable and future clients.
- **Simplicity**: Pass. Uses focused services and plain deterministic renderers instead of a new template engine, artifact store, or deployment subsystem.

## Project Structure

### Documentation (this feature)

```text
specs/009-server-bundle-generation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── builder-bundle-api.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Platform.PackageCatalog.Core/
│   ├── Builder/
│   │   ├── BundleGenerationService.cs
│   │   ├── BundleGenerationModels.cs
│   │   ├── BundleFindingPolicy.cs
│   │   └── Renderers/
│   ├── Compatibility/
│   └── Packages/
└── Elsa.Platform.Api/
    ├── Public/Builder/
    │   ├── BuilderContracts.cs
    │   └── BuilderEndpoints.cs
    └── Workspace/
        └── WorkspaceBuilderEndpoints.cs

tests/
├── Elsa.Platform.PackageCatalog.Core.Tests/
│   └── BuilderBundleGenerationTests.cs
└── Elsa.Platform.Api.Tests/
    ├── PublicBuilderBundleApiTests.cs
    └── WorkspaceBuilderBundleApiTests.cs
```

**Structure Decision**: Keep bundle generation in `Elsa.Platform.PackageCatalog.Core/Builder` so rendering, validation, and findings are reusable by public, workspace, future CLI, and saved-configuration entry points. Keep HTTP request/response records in the existing builder API namespaces. No persistence project changes are planned because generated files are ephemeral.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
