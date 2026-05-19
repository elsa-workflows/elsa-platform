# Tasks: Saved Runtime Configurations

**Input**: Design documents from `/specs/011-saved-runtime-configurations/`

## Phase 1: Setup

- [X] T001 [P] Create API test file `tests/Elsa.Catalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T002 [P] Create core test file `tests/Elsa.Catalog.Core.Tests/RuntimeConfigurationServiceTests.cs`
- [X] T003 [P] Create persistence test file `tests/Elsa.Catalog.Persistence.EntityFrameworkCore.Tests/RuntimeConfigurationPersistenceTests.cs`

## Phase 2: Foundation

- [X] T004 Define runtime configuration models in `src/Elsa.Catalog.Core/RuntimeConfigurations/RuntimeConfigurationModels.cs`
- [X] T005 Define runtime configuration store contracts in `src/Elsa.Catalog.Core/RuntimeConfigurations/RuntimeConfigurationStore.cs`
- [X] T006 Add EF Core entities and mappings in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T007 Add SQLite and SQL Server migrations in `src/Elsa.Catalog.Persistence.SqliteMigrations/Migrations/` and `src/Elsa.Catalog.Persistence.SqlServerMigrations/Migrations/`
- [X] T008 Register runtime configuration services and store in `src/Elsa.Catalog.Api/Program.cs`

## Phase 3: User Story 1 - Save And Reopen (Priority: P1)

- [X] T009 [US1] Add API tests for create/list/get workspace configuration in `tests/Elsa.Catalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T010 [US1] Implement create/list/get service methods in `src/Elsa.Catalog.Core/RuntimeConfigurations/RuntimeConfigurationService.cs`
- [X] T011 [US1] Implement EF store methods in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs`
- [X] T012 [US1] Add workspace API endpoints in `src/Elsa.Catalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`

## Phase 4: User Story 2 - Clone And Edit (Priority: P1)

- [X] T013 [US2] Add API tests for update, delete, and clone in `tests/Elsa.Catalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T014 [US2] Implement update/delete/clone service methods in `src/Elsa.Catalog.Core/RuntimeConfigurations/RuntimeConfigurationService.cs`
- [X] T015 [US2] Implement update/delete/clone EF store methods in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs`
- [X] T016 [US2] Add update/delete/clone endpoints in `src/Elsa.Catalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`
- [X] T017 [US2] Add bundle-from-configuration endpoint in `src/Elsa.Catalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`

## Phase 5: User Story 3 - Explicit Versions (Priority: P2)

- [X] T018 [US3] Add version snapshot tests in `tests/Elsa.Catalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T019 [US3] Implement immutable version creation/listing in `src/Elsa.Catalog.Core/RuntimeConfigurations/RuntimeConfigurationService.cs`
- [X] T020 [US3] Implement version persistence in `src/Elsa.Catalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs`
- [X] T021 [US3] Add version endpoints in `src/Elsa.Catalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`

## Phase 6: Polish

- [X] T022 Update API examples in `src/Elsa.Catalog.Api/Elsa.Catalog.Api.http`
- [X] T023 Run `dotnet build Elsa.PackageCatalog.sln --no-restore` against `Elsa.PackageCatalog.sln`
- [X] T024 Run `dotnet test Elsa.PackageCatalog.sln --no-build` against `Elsa.PackageCatalog.sln`

## Dependencies

- Foundation blocks all stories.
- US1 and US2 are MVP.
- US3 can follow once drafts are stable.
