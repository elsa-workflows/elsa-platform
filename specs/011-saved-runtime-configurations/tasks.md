# Tasks: Saved Runtime Configurations

**Input**: Design documents from `/specs/011-saved-runtime-configurations/`

## Phase 1: Setup

- [X] T001 [P] Create API test file `tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T002 [P] Create core test file `tests/Elsa.Platform.PackageCatalog.Core.Tests/RuntimeConfigurationServiceTests.cs`
- [X] T003 [P] Create persistence test file `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/RuntimeConfigurationPersistenceTests.cs`

## Phase 2: Foundation

- [X] T004 Define runtime configuration models in `src/Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/RuntimeConfigurationModels.cs`
- [X] T005 Define runtime configuration store contracts in `src/Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/RuntimeConfigurationStore.cs`
- [X] T006 Add EF Core entities and mappings in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [X] T007 Add SQLite and SQL Server migrations in `src/Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations/Migrations/` and `src/Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations/Migrations/`
- [X] T008 Register runtime configuration services and store in `src/Elsa.Platform.PackageCatalog.Api/Program.cs`

## Phase 3: User Story 1 - Save And Reopen (Priority: P1)

- [X] T009 [US1] Add API tests for create/list/get workspace configuration in `tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T010 [US1] Implement create/list/get service methods in `src/Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/RuntimeConfigurationService.cs`
- [X] T011 [US1] Implement EF store methods in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs`
- [X] T012 [US1] Add workspace API endpoints in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`

## Phase 4: User Story 2 - Clone And Edit (Priority: P1)

- [X] T013 [US2] Add API tests for update, delete, and clone in `tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T014 [US2] Implement update/delete/clone service methods in `src/Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/RuntimeConfigurationService.cs`
- [X] T015 [US2] Implement update/delete/clone EF store methods in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs`
- [X] T016 [US2] Add update/delete/clone endpoints in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`
- [X] T017 [US2] Add bundle-from-configuration endpoint in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`

## Phase 5: User Story 3 - Explicit Versions (Priority: P2)

- [X] T018 [US3] Add version snapshot tests in `tests/Elsa.Platform.PackageCatalog.Api.Tests/WorkspaceRuntimeConfigurationApiTests.cs`
- [X] T019 [US3] Implement immutable version creation/listing in `src/Elsa.Platform.PackageCatalog.Core/RuntimeConfigurations/RuntimeConfigurationService.cs`
- [X] T020 [US3] Implement version persistence in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/RuntimeConfigurationStore.cs`
- [X] T021 [US3] Add version endpoints in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeConfigurationEndpoints.cs`

## Phase 6: Polish

- [X] T022 Update API examples in `src/Elsa.Platform.PackageCatalog.Api/Elsa.Platform.PackageCatalog.Api.http`
- [X] T023 Run `dotnet build Elsa.Platform.sln --no-restore` against `Elsa.Platform.sln`
- [X] T024 Run `dotnet test Elsa.Platform.sln --no-build` against `Elsa.Platform.sln`

## Dependencies

- Foundation blocks all stories.
- US1 and US2 are MVP.
- US3 can follow once drafts are stable.
