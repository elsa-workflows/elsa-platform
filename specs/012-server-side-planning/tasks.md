# Tasks: Server-Side Planning

**Input**: Design documents from `/specs/012-server-side-planning/`

## Phase 1: Setup

- [X] T001 [P] Create planner core test file `tests/Elsa.Platform.PackageCatalog.Core.Tests/BuilderPlannerTests.cs`
- [X] T002 [P] Create planner API test file `tests/Elsa.Platform.PackageCatalog.Api.Tests/BuilderPlanApiTests.cs`

## Phase 2: Foundation

- [X] T003 Define planner models in `src/Elsa.Platform.PackageCatalog.Core/Builder/Planner/BuilderPlannerModels.cs`
- [X] T004 Implement deterministic planner service skeleton in `src/Elsa.Platform.PackageCatalog.Core/Builder/Planner/BuilderPlannerService.cs`
- [X] T005 Register planner service in `src/Elsa.Platform.PackageCatalog.Api/Program.cs`

## Phase 3: User Story 1 - Resolve Builder Intent (Priority: P1)

- [X] T006 [US1] Add dependency closure tests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/BuilderPlannerTests.cs`
- [X] T007 [US1] Implement package and feature dependency closure in `src/Elsa.Platform.PackageCatalog.Core/Builder/Planner/BuilderPlannerService.cs`
- [X] T008 [US1] Add `POST /api/builder/plan` DTOs in `src/Elsa.Platform.PackageCatalog.Api/Public/Builder/BuilderContracts.cs`
- [X] T009 [US1] Add `POST /api/builder/plan` endpoint in `src/Elsa.Platform.PackageCatalog.Api/Public/Builder/BuilderEndpoints.cs`

## Phase 4: User Story 2 - Shared Plan Across Resolve And Bundle (Priority: P1)

- [X] T010 [US2] Add tests for matching plan/resolve/bundle findings in `tests/Elsa.Platform.PackageCatalog.Core.Tests/BuilderPlannerTests.cs`
- [X] T011 [US2] Integrate planner into compatibility resolve flow in `src/Elsa.Platform.PackageCatalog.Api/Public/Builder/BuilderEndpoints.cs`
- [X] T012 [US2] Integrate planner into bundle generation in `src/Elsa.Platform.PackageCatalog.Core/Builder/BundleGenerationService.cs`

## Phase 5: User Story 3 - Frontend Presentation Only (Priority: P2)

- [X] T013 [US3] Add API tests for resolved state and auto-added response shape in `tests/Elsa.Platform.PackageCatalog.Api.Tests/BuilderPlanApiTests.cs`
- [X] T014 [US3] Add workspace planner endpoint in `src/Elsa.Platform.PackageCatalog.Api/Workspace/WorkspaceBuilderEndpoints.cs`
- [X] T015 [US3] Document frontend migration notes in `specs/012-server-side-planning/quickstart.md`

## Phase 6: Polish

- [X] T016 Add planner examples in `src/Elsa.Platform.PackageCatalog.Api/Elsa.Platform.PackageCatalog.Api.http`
- [X] T017 Run `dotnet build Elsa.Platform.sln --no-restore` against `Elsa.Platform.sln`
- [X] T018 Run `dotnet test Elsa.Platform.sln --no-build` against `Elsa.Platform.sln`

## Dependencies

- Foundation blocks all stories.
- US1 and US2 are MVP.
