# Tasks: Valence Control Package Catalog

**Input**: Design documents from `/specs/001-package-catalog/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml), [quickstart.md](./quickstart.md)

**Tests**: Required by the specification testing strategy and quickstart coverage. Test tasks are listed before implementation tasks in each phase.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. Foundational tasks create shared contracts, persistence, auth, and test infrastructure needed by all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and does not depend on incomplete tasks.
- **[Story]**: User story label for story-phase tasks only.
- Every task includes an exact file path.

## Phase 1: Setup

**Purpose**: Create the .NET solution, onion-style project structure, package references, shared build settings, and test projects.

- [X] T001 Create solution file and source/test directories in `ValenceControl.sln`
- [X] T002 Create manifest contract project in `src/ValenceControl.PackageManifests/ValenceControl.PackageManifests.csproj`
- [X] T003 Create catalog core project in `src/ValenceControl.PackageCatalog.Core/ValenceControl.PackageCatalog.Core.csproj`
- [X] T004 Create API project in `src/ValenceControl.Api/ValenceControl.Api.csproj`
- [X] T005 Create EF Core persistence project in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.csproj`
- [X] T006 Create NuGet packaging adapter project in `src/ValenceControl.PackageCatalog.Sources.NuGet/ValenceControl.PackageCatalog.Sources.NuGet.csproj`
- [X] T007 Create manifest contract test project in `tests/ValenceControl.PackageManifests.Tests/ValenceControl.PackageManifests.Tests.csproj`
- [X] T008 Create core test project in `tests/ValenceControl.PackageCatalog.Core.Tests/ValenceControl.PackageCatalog.Core.Tests.csproj`
- [X] T009 Create API integration test project in `tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj`
- [X] T010 Create EF Core persistence test project in `tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj`
- [X] T011 Create NuGet packaging test project in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests.csproj`
- [X] T012 Create shared test helper project in `tests/ValenceControl.PackageCatalog.Testing/ValenceControl.PackageCatalog.Testing.csproj`
- [X] T013 Configure nullable reference types, implicit usings, analyzers, and deterministic builds in `Directory.Build.props`
- [X] T014 Add solution-wide package version management for ASP.NET Core, EF Core SQLite, NuGet.Protocol, JSON Schema validation, xUnit, FluentAssertions, and WebApplicationFactory in `Directory.Packages.props`

## Phase 2: Foundational

**Purpose**: Build shared contracts, domain model, persistence, API shell, auth, diagnostics, and test fixtures that block all story work.

**Critical**: No user story work begins until this phase is complete.

### Tests

- [X] T015 [P] Add manifest serialization and extension data test skeletons in `tests/ValenceControl.PackageManifests.Tests/ManifestSerializationTests.cs`
- [X] T016 [P] Add manifest schema validation test skeletons for valid, invalid, unsupported, and oversized manifests in `tests/ValenceControl.PackageManifests.Tests/ManifestSchemaValidationTests.cs`
- [X] T017 [P] Add public visibility rule tests for valid, approved, listed, rejected, invalid, unlisted, and suspicious versions in `tests/ValenceControl.PackageCatalog.Core.Tests/PublicCatalogVisibilityTests.cs`
- [X] T018 [P] Add immutable package-version behavior tests in `tests/ValenceControl.PackageCatalog.Core.Tests/PackageVersionImmutabilityTests.cs`
- [X] T019 [P] Add API key authentication tests in `tests/ValenceControl.Api.Tests/AdminApiAuthenticationTests.cs`
- [X] T020 [P] Add SQLite persistence mapping smoke tests in `tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/CatalogDbContextMappingTests.cs`
- [X] T021 [P] Add package archive safety tests proving assemblies are not loaded or executed in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/PackageArchiveManifestReaderSafetyTests.cs`

### Implementation

- [X] T022 [P] Implement manifest schema version constants in `src/ValenceControl.PackageManifests/ManifestSchemaVersions.cs`
- [X] T023 [P] Implement shared extension data base model in `src/ValenceControl.PackageManifests/ExtensibleManifestObject.cs`
- [X] T024 [P] Implement `ElsaPackageManifest` DTO in `src/ValenceControl.PackageManifests/ElsaPackageManifest.cs`
- [X] T025 [P] Implement feature and setting manifest DTOs in `src/ValenceControl.PackageManifests/FeatureManifest.cs` and `src/ValenceControl.PackageManifests/FeatureSettingManifest.cs`
- [X] T026 [P] Implement compatibility, dependency, conflict, license, and documentation manifest DTOs in `src/ValenceControl.PackageManifests/Compatibility/CompatibilityManifest.cs`
- [X] T027 [P] Implement validation result DTOs in `src/ValenceControl.PackageManifests/Validation/ManifestValidationResult.cs`
- [X] T028 [P] Add embedded v1 JSON Schema resource in `src/ValenceControl.PackageManifests/Schemas/elsa-package-manifest.v1.json`
- [X] T029 Implement manifest JSON serialization options in `src/ValenceControl.PackageManifests/ManifestJsonSerializerOptions.cs`
- [X] T030 Implement manifest validation service with schema lookup, 1 MB size check, version-range checks, and extension support in `src/ValenceControl.PackageManifests/Validation/ManifestValidator.cs`
- [X] T031 [P] Implement core enums and value objects in `src/ValenceControl.PackageCatalog.Core/Packages/PackageCatalogEnums.cs`
- [X] T032 [P] Implement `PackageSource`, `Package`, and `PackageVersion` core models in `src/ValenceControl.PackageCatalog.Core/Packages/PackageModels.cs`
- [X] T033 [P] Implement `FeatureRecord` and `FeatureSettingRecord` core models in `src/ValenceControl.PackageCatalog.Core/Manifests/FeatureProjectionModels.cs`
- [X] T034 [P] Implement `ManifestValidationResultRecord`, `ApprovalRecord`, `SyncRun`, and `SyncRunItem` core models in `src/ValenceControl.PackageCatalog.Core/Sync/SyncModels.cs`
- [X] T035 Implement public visibility policy in `src/ValenceControl.PackageCatalog.Core/Packages/PublicCatalogVisibilityPolicy.cs`
- [X] T036 Implement immutable package-version update policy and suspicious hash detection in `src/ValenceControl.PackageCatalog.Core/Packages/PackageVersionPolicy.cs`
- [X] T037 Implement EF Core `CatalogDbContext` and DbSet declarations in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/CatalogDbContext.cs`
- [X] T038 Implement EF Core entity mappings and relational constraints in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T039 Add initial EF Core migration for package sources, packages, versions, feature projections, validation results, approval records, sync runs, and sync run items in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/Migrations/InitialCatalogCreate.cs`
- [X] T040 Implement repository/query abstractions used by API and sync flows in `src/ValenceControl.PackageCatalog.Core/Persistence/CatalogStoreContracts.cs`
- [X] T041 Implement EF Core catalog store in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/EfCoreCatalogStore.cs`
- [X] T042 Implement API host composition, service registration, problem details, OpenAPI, and health endpoint in `src/ValenceControl.Api/Program.cs`
- [X] T043 Implement API key authentication handler in `src/ValenceControl.Api/Authentication/ApiKeyAuthenticationHandler.cs`
- [X] T044 Implement admin authorization policy registration in `src/ValenceControl.Api/Authentication/AdminAuthorization.cs`
- [X] T045 Implement sync diagnostics logger abstractions in `src/ValenceControl.PackageCatalog.Core/Sync/SyncDiagnostics.cs`
- [X] T046 Add controlled manifest and package fixture builders in `tests/ValenceControl.PackageCatalog.Testing/ManifestFixtureBuilder.cs`
- [X] T047 Add controlled NuGet archive fixture builder that creates `.nupkg` files without loading assemblies in `tests/ValenceControl.PackageCatalog.Testing/NuGetPackageFixtureBuilder.cs`
- [X] T048 Verify foundational tests fail for missing implementation or pass after implementation with `dotnet test` in `ValenceControl.sln`

**Checkpoint**: Shared contracts, core models, persistence, auth, safety checks, and diagnostics are ready.

## Phase 3: User Story 1 - Discover Approved Packages (Priority: P1)

**Goal**: Runtime Builder users can browse public packages, package versions, features, and feature settings while hidden states remain invisible.

**Independent Test**: Seed approved/listed/valid, invalid, rejected, unlisted, and suspicious package versions and verify public package and feature APIs return only valid, approved, listed, non-suspicious records.

### Tests

- [X] T049 [P] [US1] Add public package listing contract tests for `GET /api/packages` in `tests/ValenceControl.Api.Tests/PublicPackagesApiTests.cs`
- [X] T050 [P] [US1] Add public package details and versions contract tests for `GET /api/packages/{packageId}` and `GET /api/packages/{packageId}/versions` in `tests/ValenceControl.Api.Tests/PublicPackageDetailsApiTests.cs`
- [X] T051 [P] [US1] Add public package version details contract tests for `GET /api/packages/{packageId}/versions/{version}` in `tests/ValenceControl.Api.Tests/PublicPackageVersionApiTests.cs`
- [X] T052 [P] [US1] Add public feature listing and details contract tests for `GET /api/features` and `GET /api/features/{featureId}` in `tests/ValenceControl.Api.Tests/PublicFeaturesApiTests.cs`
- [X] T053 [P] [US1] Add query projection tests for package, version, feature, and setting summaries in `tests/ValenceControl.PackageCatalog.Core.Tests/PublicCatalogQueryServiceTests.cs`

### Implementation

- [X] T054 [P] [US1] Implement public package response models in `src/ValenceControl.Api/Public/Packages/PublicPackageContracts.cs`
- [X] T055 [P] [US1] Implement public feature response models in `src/ValenceControl.Api/Public/Features/PublicFeatureContracts.cs`
- [X] T056 [US1] Implement public catalog query service in `src/ValenceControl.PackageCatalog.Core/Packages/PublicCatalogQueryService.cs`
- [X] T057 [US1] Implement package projection queries in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/PublicCatalogQueries.cs`
- [X] T058 [US1] Implement public package endpoints in `src/ValenceControl.Api/Public/Packages/PublicPackageEndpoints.cs`
- [X] T059 [US1] Implement public feature endpoints in `src/ValenceControl.Api/Public/Features/PublicFeatureEndpoints.cs`
- [X] T060 [US1] Register public endpoint modules in `src/ValenceControl.Api/Program.cs`
- [X] T061 [US1] Add public API seed helpers for visible and hidden package states in `tests/ValenceControl.PackageCatalog.Testing/PublicCatalogSeedData.cs`
- [X] T062 [US1] Verify US1 public discovery tests with `dotnet test --filter Public` in `ValenceControl.sln`

**Checkpoint**: Public package and feature discovery works independently with seeded catalog data.

## Phase 4: User Story 2 - Configure Package Sources (Priority: P1)

**Goal**: Catalog administrators can create, update, list, and delete explicitly configured unauthenticated NuGet feed sources using case-insensitive glob include/exclude patterns.

**Independent Test**: Use admin APIs to manage sources and verify enabled state, URL validation, approval policy, unauthenticated-only constraint, and exclude-wins pattern matching.

### Tests

- [X] T063 [P] [US2] Add admin package source contract tests for `GET /api/admin/sources` and `POST /api/admin/sources` in `tests/ValenceControl.Api.Tests/AdminSourcesApiTests.cs`
- [X] T064 [P] [US2] Add admin source update and delete contract tests for `PUT /api/admin/sources/{id}` and `DELETE /api/admin/sources/{id}` in `tests/ValenceControl.Api.Tests/AdminSourceMutationApiTests.cs`
- [X] T065 [P] [US2] Add source validation tests for URL, required include patterns, approval policy, and unauthenticated-only feeds in `tests/ValenceControl.PackageCatalog.Core.Tests/PackageSourceValidationTests.cs`
- [X] T066 [P] [US2] Add case-insensitive glob matching tests with exclude precedence in `tests/ValenceControl.PackageCatalog.Core.Tests/PackageSourcePatternMatcherTests.cs`

### Implementation

- [X] T067 [P] [US2] Implement admin source request and response contracts in `src/ValenceControl.Api/Admin/Sources/AdminSourceContracts.cs`
- [X] T068 [US2] Implement package source validation rules in `src/ValenceControl.PackageCatalog.Core/Sources/PackageSourceValidator.cs`
- [X] T069 [US2] Implement case-insensitive glob matcher with exclude precedence in `src/ValenceControl.PackageCatalog.Core/Sources/PackageSourcePatternMatcher.cs`
- [X] T070 [US2] Implement source management service in `src/ValenceControl.PackageCatalog.Core/Sources/PackageSourceService.cs`
- [X] T071 [US2] Implement EF Core source repository methods in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/PackageSourceStore.cs`
- [X] T072 [US2] Implement admin source endpoints in `src/ValenceControl.Api/Admin/Sources/AdminSourceEndpoints.cs`
- [X] T073 [US2] Register admin source endpoints in `src/ValenceControl.Api/Program.cs`
- [X] T074 [US2] Verify US2 source management tests with `dotnet test --filter Sources` in `ValenceControl.sln`

**Checkpoint**: Admin source management works independently and source scope is explicit.

## Phase 5: User Story 3 - Synchronize Manifests (Priority: P1)

**Goal**: Scheduled and manual sync discover matching package versions, download only needed versions, extract manifests safely, validate, persist results, and isolate item failures.

**Independent Test**: Run sync against controlled NuGet package fixtures containing valid, invalid, missing-manifest, unchanged, oversized, and suspicious package versions and verify stored versions, validation results, sync run items, and summary counters.

### Tests

- [X] T075 [P] [US3] Add package archive manifest extraction tests for root and fallback manifest paths in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/PackageArchiveManifestReaderTests.cs`
- [X] T076 [P] [US3] Add missing, multiple, oversized, malformed, and identity-mismatch manifest tests in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/PackageArchiveManifestValidationTests.cs`
- [X] T077 [P] [US3] Add NuGet source version discovery tests with include/exclude filtering in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/NuGetPackageSourceClientTests.cs`
- [X] T078 [P] [US3] Add sync orchestration tests for valid, invalid, failed, unchanged, and suspicious items in `tests/ValenceControl.PackageCatalog.Core.Tests/PackageSyncServiceTests.cs`
- [X] T079 [P] [US3] Add admin sync trigger and sync history API tests in `tests/ValenceControl.Api.Tests/AdminSyncApiTests.cs`
- [X] T080 [P] [US3] Add persistence tests for immutable version records and sync run item diagnostics in `tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs`

### Implementation

- [X] T081 [P] [US3] Implement package archive manifest reader without assembly loading in `src/ValenceControl.PackageCatalog.Sources.NuGet/PackageArchiveManifestReader.cs`
- [X] T082 [P] [US3] Implement NuGet feed version discovery client in `src/ValenceControl.PackageCatalog.Sources.NuGet/NuGetPackageSourceClient.cs`
- [X] T083 [P] [US3] Implement NuGet package downloader for new versions only in `src/ValenceControl.PackageCatalog.Sources.NuGet/NuGetSyncPackageDownloader.cs`
- [X] T084 [US3] Implement manifest ingestion and projection mapper in `src/ValenceControl.PackageCatalog.Core/Manifests/ManifestIngestionService.cs`
- [X] T085 [US3] Implement package sync service with item-level error isolation and summary counters in `src/ValenceControl.PackageCatalog.Core/Sync/PackageSyncService.cs`
- [X] T086 [US3] Implement sync run concurrency guard for source/package scopes in `src/ValenceControl.PackageCatalog.Core/Sync/SyncConcurrencyGuard.cs`
- [X] T087 [US3] Implement scheduled sync hosted service in `src/ValenceControl.Api/Admin/Sync/ScheduledSyncHostedService.cs`
- [X] T088 [US3] Implement manual sync request contracts in `src/ValenceControl.Api/Admin/Sync/AdminSyncContracts.cs`
- [X] T089 [US3] Implement admin sync trigger and sync-run endpoints in `src/ValenceControl.Api/Admin/Sync/AdminSyncEndpoints.cs`
- [X] T090 [US3] Implement EF Core sync run and sync item persistence methods in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/SyncRunStore.cs`
- [X] T091 [US3] Register NuGet packaging and sync services in `src/ValenceControl.Api/Program.cs`
- [X] T092 [US3] Verify US3 sync tests with `dotnet test --filter Sync` in `ValenceControl.sln`

**Checkpoint**: Sync can ingest manifests safely and produce durable diagnostics.

### Follow-up: Feed Sync Hardening

- [X] T092a [US3] Add latest-preview-only version discovery tests in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/NuGetPackageSourceClientTests.cs`
- [X] T092b [US3] Implement latest-preview-only source policy in `src/ValenceControl.PackageCatalog.Sources.NuGet/NuGetPackageSourceClient.cs`
- [X] T092c [US3] Expose latest-preview-only source policy through admin source API/UI contracts in `src/ValenceControl.Api/Admin/Sources/AdminSourceContracts.cs` and `src/ValenceControl.Console/src/features/sources/sourceModels.ts`
- [X] T092d [US3] Add background manual sync trigger tests in `tests/ValenceControl.Api.Tests/AdminSyncApiTests.cs`
- [X] T092e [US3] Implement queued background manual sync execution in `src/ValenceControl.Api/Admin/Sync` so admin requests are not tied to package download cancellation.

## Phase 6: User Story 4 - Approve Catalog Entries (Priority: P2)

**Goal**: Catalog administrators can approve or reject packages and versions while validation, approval, and listing stay separate.

**Independent Test**: Index valid and invalid versions under manual and auto-approve policies, approve/reject package and version records, and verify public visibility changes only when validity, approval, and listing all permit it.

### Tests

- [X] T093 [P] [US4] Add approval policy tests for manual and auto-approve sources in `tests/ValenceControl.PackageCatalog.Core.Tests/ApprovalPolicyTests.cs`
- [X] T094 [P] [US4] Add admin package review API tests for `GET /api/admin/packages` and `GET /api/admin/packages/{packageId}` in `tests/ValenceControl.Api.Tests/AdminPackagesApiTests.cs`
- [X] T095 [P] [US4] Add package and version approve/reject API tests in `tests/ValenceControl.Api.Tests/AdminApprovalApiTests.cs`
- [X] T096 [P] [US4] Add admin validation details API tests for `GET /api/admin/packages/{packageId}/versions/{version}/validation` in `tests/ValenceControl.Api.Tests/AdminValidationApiTests.cs`

### Implementation

- [X] T097 [P] [US4] Implement approval request and admin package response contracts in `src/ValenceControl.Api/Admin/Packages/AdminPackageContracts.cs`
- [X] T098 [US4] Implement approval service for package-level and version-level decisions in `src/ValenceControl.PackageCatalog.Core/Approvals/ApprovalService.cs`
- [X] T099 [US4] Implement manual-source new-version pending behavior in `src/ValenceControl.PackageCatalog.Core/Approvals/ApprovalPolicy.cs`
- [X] T100 [US4] Implement EF Core approval record persistence and current-state queries in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/ApprovalStore.cs`
- [X] T101 [US4] Implement admin package review endpoints in `src/ValenceControl.Api/Admin/Packages/AdminPackageEndpoints.cs`
- [X] T102 [US4] Implement admin approval and rejection endpoints in `src/ValenceControl.Api/Admin/Packages/AdminApprovalEndpoints.cs`
- [X] T103 [US4] Implement admin validation details endpoint in `src/ValenceControl.Api/Admin/Packages/AdminValidationEndpoints.cs`
- [X] T104 [US4] Register admin package endpoints in `src/ValenceControl.Api/Program.cs`
- [X] T105 [US4] Verify US4 approval tests with `dotnet test --filter Approval` in `ValenceControl.sln`

**Checkpoint**: Admin review and approval workflows work independently from manifest validity.

## Phase 7: User Story 5 - Check Compatibility (Priority: P2)

**Goal**: Runtime Builder and validation clients can check selected package versions, selected features, and runtime targets and receive pass, warning, or error findings.

**Independent Test**: Submit selections with existing, missing, unapproved, invalid, compatible, incompatible, and unknown compatibility records and verify deterministic findings.

### Tests

- [X] T106 [P] [US5] Add compatibility service tests for missing, unapproved, invalid, unlisted, and suspicious package versions in `tests/ValenceControl.PackageCatalog.Core.Tests/CompatibilityCheckServiceTests.cs`
- [X] T107 [P] [US5] Add version range compatibility tests for Elsa and Docker image versions in `tests/ValenceControl.PackageCatalog.Core.Tests/CompatibilityRangeTests.cs`
- [X] T108 [P] [US5] Add direct package and feature conflict tests in `tests/ValenceControl.PackageCatalog.Core.Tests/CompatibilityConflictTests.cs`
- [X] T109 [P] [US5] Add public compatibility API contract tests for `POST /api/compatibility/check` in `tests/ValenceControl.Api.Tests/PublicCompatibilityApiTests.cs`

### Implementation

- [X] T110 [P] [US5] Implement compatibility request and response contracts in `src/ValenceControl.Api/Public/Compatibility/CompatibilityContracts.cs`
- [X] T111 [US5] Implement version range evaluator for Elsa and Docker image ranges in `src/ValenceControl.PackageCatalog.Core/Compatibility/VersionRangeEvaluator.cs`
- [X] T112 [US5] Implement compatibility check service with package existence, approval, listing, validity, warning, and conflict findings in `src/ValenceControl.PackageCatalog.Core/Compatibility/CompatibilityCheckService.cs`
- [X] T113 [US5] Implement EF Core compatibility read queries in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/CompatibilityQueries.cs`
- [X] T114 [US5] Implement public compatibility endpoint in `src/ValenceControl.Api/Public/Compatibility/CompatibilityEndpoints.cs`
- [X] T115 [US5] Register compatibility endpoint in `src/ValenceControl.Api/Program.cs`
- [X] T116 [US5] Verify US5 compatibility tests with `dotnet test --filter Compatibility` in `ValenceControl.sln`

**Checkpoint**: Compatibility checks return actionable findings without full dependency resolution.

## Phase 8: User Story 6 - Share Manifest Contracts (Priority: P2)

**Goal**: Manifest tooling, catalog ingestion, and future runtime validation can all use the shared manifest contract without catalog persistence or runtime internals.

**Independent Test**: Serialize and deserialize representative manifests with extension data and future fields, validate supported and unsupported schema versions, and confirm the package has no catalog persistence or runtime infrastructure references.

### Tests

- [X] T117 [P] [US6] Add public DTO API shape tests for all manifest contract types in `tests/ValenceControl.PackageManifests.Tests/ManifestContractShapeTests.cs`
- [X] T118 [P] [US6] Add extension metadata round-trip tests at package, feature, setting, compatibility, license, documentation, dependency, conflict, and validation levels in `tests/ValenceControl.PackageManifests.Tests/ExtensionDataRoundTripTests.cs`
- [X] T119 [P] [US6] Add schema resource discovery tests for `schemas/elsa-package-manifest.v1.json` in `tests/ValenceControl.PackageManifests.Tests/EmbeddedSchemaResourceTests.cs`
- [X] T120 [P] [US6] Add dependency boundary tests ensuring `ValenceControl.PackageManifests` does not reference catalog persistence or runtime internals in `tests/ValenceControl.PackageManifests.Tests/ManifestPackageDependencyTests.cs`

### Implementation

- [X] T121 [US6] Refine all manifest DTO XML documentation and examples in `src/ValenceControl.PackageManifests/ElsaPackageManifest.cs`
- [X] T122 [US6] Refine feature and setting DTO XML documentation and examples in `src/ValenceControl.PackageManifests/FeatureManifest.cs`
- [X] T123 [US6] Refine compatibility, dependency, conflict, license, and documentation DTO XML documentation in `src/ValenceControl.PackageManifests/Compatibility/CompatibilityManifest.cs`
- [X] T124 [US6] Add representative valid manifest sample in `src/ValenceControl.PackageManifests/Schemas/examples/elsa-package.valid.v1.json`
- [X] T125 [US6] Add representative invalid manifest sample in `src/ValenceControl.PackageManifests/Schemas/examples/elsa-package.invalid.v1.json`
- [X] T126 [US6] Add package README with schema versioning and extension metadata guidance in `src/ValenceControl.PackageManifests/README.md`
- [X] T127 [US6] Verify US6 manifest contract tests with `dotnet test --filter Manifest` in `ValenceControl.sln`

**Checkpoint**: The shared manifest contract package is stable, documented, dependency-light, and independently testable.

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Complete documentation, operational checks, quickstart validation, and whole-solution verification.

- [X] T128 [P] Update project README with overview, local development, sync safety, and API key instructions in `README.md`
- [X] T129 [P] Add appsettings template for SQLite, API key auth, and sync scheduling in `src/ValenceControl.Api/appsettings.Development.json`
- [X] T130 [P] Add structured logging configuration for sync, validation, approval, and suspicious changes in `src/ValenceControl.Api/appsettings.json`
- [X] T131 Add startup database migration or initialization behavior in `src/ValenceControl.Api/Program.cs`
- [X] T132 Add quickstart validation script for build, test, run, source creation, sync, approval, public discovery, and compatibility check in `scripts/quickstart-verify.sh`
- [X] T133 Verify no package-processing code loads assemblies by reviewing `src/ValenceControl.PackageCatalog.Sources.NuGet/PackageArchiveManifestReader.cs` and test coverage in `tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/PackageArchiveManifestReaderSafetyTests.cs`
- [X] T134 Verify public APIs hide invalid, unapproved, rejected, suspicious, and unlisted records with `dotnet test --filter Public` in `ValenceControl.sln`
- [X] T135 Verify manifest schema versioning, extension metadata, and 1 MB limit with `dotnet test --filter Manifest` in `ValenceControl.sln`
- [X] T136 Verify admin diagnostics expose sync runs, validation errors, approval decisions, and suspicious changes with `dotnet test --filter Admin` in `ValenceControl.sln`
- [X] T137 Run full solution verification with `dotnet test` in `ValenceControl.sln`
- [X] T138 Run quickstart verification script in `scripts/quickstart-verify.sh`

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup** has no prerequisites.
- **Phase 2 Foundational** depends on Phase 1 and blocks all user stories.
- **US1 Public Discovery** depends on Phase 2 only and is the MVP slice.
- **US2 Source Management** depends on Phase 2 only and can run in parallel with US1 after foundation.
- **US3 Synchronization** depends on Phase 2 and benefits from US2 source validation, but can be tested independently with seeded source records.
- **US4 Approval Workflow** depends on Phase 2 and can use seeded package/version records.
- **US5 Compatibility Check** depends on Phase 2 and can use seeded package/version/feature records.
- **US6 Shared Manifest Contracts** depends on Phase 2 contract skeleton and can run in parallel with API-focused stories.
- **Phase 9 Polish** depends on all desired user stories.

### User Story Dependencies

- **US1 (P1)**: Independent after foundation; recommended MVP.
- **US2 (P1)**: Independent after foundation.
- **US3 (P1)**: Independent after foundation with test fixtures; production flow uses sources from US2.
- **US4 (P2)**: Independent after foundation with seeded package data; public visibility checks integrate with US1.
- **US5 (P2)**: Independent after foundation with seeded package and feature data; uses public visibility policy.
- **US6 (P2)**: Independent after foundation; hardens the shared manifest package used by sync and validation.

### Within Each User Story

- Tests are written before implementation tasks.
- Response contracts before endpoints.
- Core services before API endpoints.
- Persistence query/store methods before endpoint integration when storage is required.
- Story verification command runs after implementation tasks.

## Parallel Opportunities

- Setup project creation tasks T002 through T012 can run in parallel after T001.
- Foundational DTO/model test skeletons T015 through T021 can run in parallel.
- Foundational model/DTO implementation tasks T022 through T028 and T031 through T034 can run in parallel by project area.
- US1 tests T049 through T053 can run in parallel.
- US2 tests T063 through T066 can run in parallel.
- US3 tests T075 through T080 can run in parallel.
- US4 tests T093 through T096 can run in parallel.
- US5 tests T106 through T109 can run in parallel.
- US6 tests T117 through T120 can run in parallel.
- Polish documentation/config tasks T128 through T130 can run in parallel.

## Parallel Example: User Story 1

```bash
Task: "T049 [P] [US1] Add public package listing contract tests in tests/ValenceControl.Api.Tests/PublicPackagesApiTests.cs"
Task: "T050 [P] [US1] Add public package details and versions contract tests in tests/ValenceControl.Api.Tests/PublicPackageDetailsApiTests.cs"
Task: "T051 [P] [US1] Add public package version details contract tests in tests/ValenceControl.Api.Tests/PublicPackageVersionApiTests.cs"
Task: "T052 [P] [US1] Add public feature listing and details contract tests in tests/ValenceControl.Api.Tests/PublicFeaturesApiTests.cs"
Task: "T053 [P] [US1] Add query projection tests in tests/ValenceControl.PackageCatalog.Core.Tests/PublicCatalogQueryServiceTests.cs"
```

## Parallel Example: User Story 3

```bash
Task: "T075 [P] [US3] Add package archive manifest extraction tests in tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/PackageArchiveManifestReaderTests.cs"
Task: "T076 [P] [US3] Add missing, multiple, oversized, malformed, and identity-mismatch manifest tests in tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/PackageArchiveManifestValidationTests.cs"
Task: "T077 [P] [US3] Add NuGet source version discovery tests in tests/ValenceControl.PackageCatalog.Sources.NuGet.Tests/NuGetPackageSourceClientTests.cs"
Task: "T078 [P] [US3] Add sync orchestration tests in tests/ValenceControl.PackageCatalog.Core.Tests/PackageSyncServiceTests.cs"
Task: "T079 [P] [US3] Add admin sync API tests in tests/ValenceControl.Api.Tests/AdminSyncApiTests.cs"
Task: "T080 [P] [US3] Add persistence diagnostics tests in tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/SyncPersistenceTests.cs"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 Setup.
2. Complete Phase 2 Foundational.
3. Complete Phase 3 User Story 1.
4. Stop and validate public package/feature discovery with seeded catalog data.

### Incremental Delivery

1. Add US1 public discovery for immediate Runtime Builder-facing value.
2. Add US2 source management so administrators can define explicit sync scope.
3. Add US3 synchronization to populate the catalog from NuGet packages.
4. Add US4 approval workflow to curate public visibility.
5. Add US5 compatibility checks for builder readiness.
6. Add US6 manifest contract hardening and documentation.
7. Finish polish and quickstart validation.

### Constitution Gates

- Manifest metadata remains explicit and versioned through `ValenceControl.PackageManifests`.
- NuGet package processing inspects archives only and never loads assemblies.
- Package version immutability and suspicious hash detection are verified before public exposure.
- Public APIs expose only valid, approved, listed, non-suspicious versions.
- Sync runs, validation failures, approval decisions, and suspicious changes are persisted and inspectable.
- New abstractions and dependencies are introduced only where required by current tasks.

## Runtime Builder Infrastructure Addendum

- [X] T136 [US1] Expose source/feed provenance and builder-grade feature metadata in `src/ValenceControl.PackageCatalog.Core/Packages/PublicCatalogQueryService.cs`
- [X] T137 [US1] Project manifest infrastructure requirements into feature records in `src/ValenceControl.PackageCatalog.Core/Manifests/FeatureProjectionModels.cs`
- [X] T138 [US1] Add Runtime Builder catalog and infrastructure provider endpoints in `src/ValenceControl.Api/Public/Builder/BuilderEndpoints.cs`
- [X] T139 [US5] Extend compatibility checks to validate selected feature dependencies and conflicts in `src/ValenceControl.PackageCatalog.Core/Compatibility/CompatibilityCheckService.cs`
- [X] T140 [US1] Add SQLite and SQL Server migrations for feature infrastructure projection in `src/ValenceControl.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/ValenceControl.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [X] T141 [P] [US1] Add builder API and infrastructure projection tests in `tests/ValenceControl.Api.Tests/PublicBuilderApiTests.cs` and `tests/ValenceControl.PackageCatalog.Core.Tests/ManifestIngestionServiceTests.cs`
