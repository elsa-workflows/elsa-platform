# Tasks: Managed Hosting Control Plane

**Input**: Design documents from `/specs/015-managed-hosting-control-plane/`

## Phase 1: Setup

- [ ] T001 [P] Create managed hosting API tests in `tests/ValenceControl.Api.Tests/ManagedHostingApiTests.cs`
- [ ] T002 [P] Create managed hosting core tests in `tests/ValenceControl.PackageCatalog.Core.Tests/ManagedHostingServiceTests.cs`

## Phase 2: Foundation

- [ ] T003 Define managed hosting models in `src/ValenceControl.PackageCatalog.Core/ManagedHosting/ManagedHostingModels.cs`
- [ ] T004 Define hosting adapter port in `src/ValenceControl.PackageCatalog.Core/ManagedHosting/ManagedHostingAdapter.cs`
- [ ] T005 Add EF mappings and migrations in `src/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [ ] T006 Register managed hosting services in `src/ValenceControl.Api/Program.cs`

## Phase 3: User Story 1 - Provision Managed Runtime (Priority: P1)

- [ ] T007 [US1] Add provision API tests in `tests/ValenceControl.Api.Tests/ManagedHostingApiTests.cs`
- [ ] T008 [US1] Implement provision service in `src/ValenceControl.PackageCatalog.Core/ManagedHosting/ManagedHostingService.cs`
- [ ] T009 [US1] Add provision endpoint in `src/ValenceControl.Api/Workspace/WorkspaceManagedHostingEndpoints.cs`

## Phase 4: User Story 2 - Runtime Lifecycle (Priority: P1)

- [ ] T010 [US2] Add lifecycle action tests in `tests/ValenceControl.Api.Tests/ManagedHostingApiTests.cs`
- [ ] T011 [US2] Implement stop/restart/delete service methods in `src/ValenceControl.PackageCatalog.Core/ManagedHosting/ManagedHostingService.cs`
- [ ] T012 [US2] Add lifecycle endpoints in `src/ValenceControl.Api/Workspace/WorkspaceManagedHostingEndpoints.cs`

## Phase 5: User Story 3 - Runtime Endpoint And Health (Priority: P2)

- [ ] T013 [US3] Add health/status tests in `tests/ValenceControl.Api.Tests/ManagedHostingApiTests.cs`
- [ ] T014 [US3] Implement health polling/status projection in `src/ValenceControl.PackageCatalog.Core/ManagedHosting/ManagedHostingService.cs`

## Phase 6: Polish

- [ ] T015 Run `dotnet build ValenceControl.sln --no-restore` against `ValenceControl.sln`
- [ ] T016 Run `dotnet test ValenceControl.sln --no-build` against `ValenceControl.sln`
