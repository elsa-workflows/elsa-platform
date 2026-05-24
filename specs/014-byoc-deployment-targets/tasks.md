# Tasks: BYOC Deployment Targets

**Input**: Design documents from `/specs/014-byoc-deployment-targets/`

## Phase 1: Setup

- [ ] T001 [P] Create BYOC API tests in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTargetApiTests.cs`
- [ ] T002 [P] Create BYOC core tests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/DeploymentTargetServiceTests.cs`
- [ ] T003 [P] Create BYOC persistence tests in `tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentTargetPersistenceTests.cs`

## Phase 2: Foundation

- [ ] T004 Define deployment target models in `src/Elsa.Platform.PackageCatalog.Core/DeploymentTargets/DeploymentTargetModels.cs`
- [ ] T005 Define cloud adapter port and fake adapter in `src/Elsa.Platform.PackageCatalog.Core/DeploymentTargets/DeploymentTargetAdapter.cs`
- [ ] T006 Add EF mappings and migrations in `src/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [ ] T007 Register services in `src/Elsa.Platform.Api/Program.cs`

## Phase 3: User Story 1 - Register Target (Priority: P1)

- [ ] T008 [US1] Add registration and validation API tests in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTargetApiTests.cs`
- [ ] T009 [US1] Implement target registration service in `src/Elsa.Platform.PackageCatalog.Core/DeploymentTargets/DeploymentTargetService.cs`
- [ ] T010 [US1] Add target endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs`

## Phase 4: User Story 2 - Preview Deployment (Priority: P1)

- [ ] T011 [US2] Add preview tests in `tests/Elsa.Platform.PackageCatalog.Core.Tests/DeploymentTargetServiceTests.cs`
- [ ] T012 [US2] Implement preview generation in `src/Elsa.Platform.PackageCatalog.Core/DeploymentTargets/DeploymentPreviewService.cs`
- [ ] T013 [US2] Add preview endpoint in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs`

## Phase 5: User Story 3 - Deploy And Track Status (Priority: P2)

- [ ] T014 [US3] Add deployment run status tests in `tests/Elsa.Platform.Api.Tests/WorkspaceDeploymentTargetApiTests.cs`
- [ ] T015 [US3] Implement deployment run service in `src/Elsa.Platform.PackageCatalog.Core/DeploymentTargets/DeploymentRunService.cs`
- [ ] T016 [US3] Add deployment run endpoints in `src/Elsa.Platform.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs`

## Phase 6: Polish

- [ ] T017 Run `dotnet build Elsa.Platform.sln --no-restore` against `Elsa.Platform.sln`
- [ ] T018 Run `dotnet test Elsa.Platform.sln --no-build` against `Elsa.Platform.sln`
