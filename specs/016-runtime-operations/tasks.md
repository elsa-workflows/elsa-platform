# Tasks: Runtime Operations

**Input**: Design documents from `/specs/016-runtime-operations/`

## Phase 1: Setup

- [ ] T001 [P] Create runtime operations API tests in `tests/Elsa.Platform.PackageCatalog.Api.Tests/RuntimeOperationsApiTests.cs`
- [ ] T002 [P] Create runtime operations core tests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/RuntimeOperationsServiceTests.cs`

## Phase 2: Foundation

- [ ] T003 Define runtime operation models in `src/Elsa.Platform.PackageCatalog.Core/RuntimeOperations/RuntimeOperationModels.cs`
- [ ] T004 Define logs, metrics, backup, and upgrade adapter ports in `src/Elsa.Platform.PackageCatalog.Core/RuntimeOperations/RuntimeOperationAdapters.cs`
- [ ] T005 Add EF mappings and migrations in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`

## Phase 3: User Story 1 - Health And Logs (Priority: P1)

- [ ] T006 [US1] Add health/log API tests in `tests/Elsa.Platform.PackageCatalog.Api.Tests/RuntimeOperationsApiTests.cs`
- [ ] T007 [US1] Implement health/log service in `src/Elsa.Platform.PackageCatalog.Core/RuntimeOperations/RuntimeOperationsService.cs`
- [ ] T008 [US1] Add health/log endpoints in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeOperationsEndpoints.cs`

## Phase 4: User Story 2 - Backup And Restore (Priority: P1)

- [ ] T009 [US2] Add backup/restore tests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/RuntimeOperationsServiceTests.cs`
- [ ] T010 [US2] Implement backup/restore service in `src/Elsa.Platform.PackageCatalog.Core/RuntimeOperations/RuntimeOperationsService.cs`
- [ ] T011 [US2] Add backup/restore endpoints in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeOperationsEndpoints.cs`

## Phase 5: User Story 3 - Upgrade And Rollback (Priority: P2)

- [ ] T012 [US3] Add upgrade/rollback tests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/RuntimeOperationsServiceTests.cs`
- [ ] T013 [US3] Implement upgrade/rollback service in `src/Elsa.Platform.PackageCatalog.Core/RuntimeOperations/RuntimeOperationsService.cs`
- [ ] T014 [US3] Add upgrade endpoints in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceRuntimeOperationsEndpoints.cs`

## Phase 6: Polish

- [ ] T015 Run `dotnet build Elsa.Platform.sln --no-restore` against `Elsa.Platform.sln`
- [ ] T016 Run `dotnet test Elsa.Platform.sln --no-build` against `Elsa.Platform.sln`
