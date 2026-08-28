# Tasks: Runtime Image Metadata API

**Input**: Design documents from `/specs/010-runtime-image-metadata-api/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/runtime-images-api.md`, `quickstart.md`

## Phase 1: Setup

- [X] T001 Inspect existing builder catalog contracts in `src/ElsaControl.Api/Public/Builder/BuilderContracts.cs`
- [X] T002 [P] Create runtime image core test file `tests/ElsaControl.PackageCatalog.Core.Tests/RuntimeImageCatalogTests.cs`
- [X] T003 [P] Create runtime image API test file `tests/ElsaControl.Api.Tests/RuntimeImageApiTests.cs`

## Phase 2: Foundation

- [X] T004 Define runtime image records in `src/ElsaControl.PackageCatalog.Core/Builder/RuntimeImageModels.cs`
- [X] T005 Define seeded runtime image catalog in `src/ElsaControl.PackageCatalog.Core/Builder/RuntimeImageCatalog.cs`
- [X] T006 Define runtime image validation rules in `src/ElsaControl.PackageCatalog.Core/Builder/RuntimeImageValidator.cs`
- [X] T007 Register runtime image catalog and validator in `src/ElsaControl.Api/Program.cs`

## Phase 3: User Story 1 - Select Runtime Images From Elsa Control Metadata (Priority: P1)

- [X] T008 [P] [US1] Add API test that `/api/builder/catalog` returns `images` with all three known slugs in `tests/ElsaControl.Api.Tests/RuntimeImageApiTests.cs`
- [X] T009 [US1] Add `images` to builder catalog response DTOs in `src/ElsaControl.Api/Public/Builder/BuilderContracts.cs`
- [X] T010 [US1] Populate `images` from runtime image catalog in `src/ElsaControl.Api/Public/Builder/BuilderEndpoints.cs`

## Phase 4: User Story 2 - Use Image Metadata During Bundle Generation (Priority: P1)

- [X] T011 [P] [US2] Add bundle generation test proving known image metadata is used in `tests/ElsaControl.PackageCatalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T012 [P] [US2] Add bundle generation test for unknown image slug error finding in `tests/ElsaControl.PackageCatalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T013 [US2] Replace bundle generator image defaults with `RuntimeImageCatalog` lookups in `src/ElsaControl.PackageCatalog.Core/Builder/BundleGenerationService.cs`

## Phase 5: User Story 3 - Separate Deployment Metadata From Presentation Copy (Priority: P2)

- [X] T014 [P] [US3] Add field-classification test for deployment-affecting fields in `tests/ElsaControl.PackageCatalog.Core.Tests/RuntimeImageCatalogTests.cs`
- [X] T015 [US3] Document frontend-owned presentation fallback fields in `specs/010-runtime-image-metadata-api/quickstart.md`

## Phase 6: User Story 4 - Validate Runtime Image Catalog Quality (Priority: P3)

- [X] T016 [P] [US4] Add validation tests for duplicate slugs and missing image references in `tests/ElsaControl.PackageCatalog.Core.Tests/RuntimeImageCatalogTests.cs`
- [X] T017 [US4] Add validation tests for default tags, duplicate env vars, and broken companion references in `tests/ElsaControl.PackageCatalog.Core.Tests/RuntimeImageCatalogTests.cs`
- [X] T018 [US4] Invoke runtime image validation during application startup in `src/ElsaControl.Api/Program.cs`

## Phase 7: Polish

- [X] T019 Update API examples in `src/ElsaControl.Api/ElsaControl.Api.http`
- [X] T020 Run `dotnet build ElsaControl.sln --no-restore` against `ElsaControl.sln`
- [X] T021 Run `dotnet test ElsaControl.sln --no-build` against `ElsaControl.sln`

## Dependencies

- Foundation blocks all stories.
- US1 and US2 are both P1 and should complete before removing Lovable deployment metadata authority.
- US3 and US4 can follow once backend image DTOs exist.
