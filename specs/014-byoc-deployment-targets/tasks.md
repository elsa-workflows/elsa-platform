# Tasks: BYOC Deployment Targets

**Input**: Design documents from `/specs/014-byoc-deployment-targets/`

## Phase 1: Setup

- [ ] T001 [P] Create BYOC API tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTargetApiTests.cs`
- [ ] T002 [P] Create BYOC core tests in `tests/ElsaControl.PackageCatalog.Core.Tests/DeploymentTargetServiceTests.cs`
- [ ] T003 [P] Create BYOC persistence tests in `tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/DeploymentTargetPersistenceTests.cs`

## Phase 2: Foundation

- [ ] T004 Define deployment target models in `src/ElsaControl.PackageCatalog.Core/DeploymentTargets/DeploymentTargetModels.cs`
- [ ] T005 Define cloud adapter port and fake adapter in `src/ElsaControl.PackageCatalog.Core/DeploymentTargets/DeploymentTargetAdapter.cs`
- [ ] T006 Add EF mappings and migrations in `src/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore/Models/CatalogModelConfiguration.cs`
- [ ] T007 Register services in `src/ElsaControl.Api/Program.cs`

## Phase 3: User Story 1 - Register Target (Priority: P1)

- [ ] T008 [US1] Add registration and validation API tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTargetApiTests.cs`
- [ ] T009 [US1] Implement target registration service in `src/ElsaControl.PackageCatalog.Core/DeploymentTargets/DeploymentTargetService.cs`
- [ ] T010 [US1] Add target endpoints in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs`

## Phase 4: User Story 2 - Preview Deployment (Priority: P1)

- [ ] T011 [US2] Add preview tests in `tests/ElsaControl.PackageCatalog.Core.Tests/DeploymentTargetServiceTests.cs`
- [ ] T012 [US2] Implement preview generation in `src/ElsaControl.PackageCatalog.Core/DeploymentTargets/DeploymentPreviewService.cs`
- [ ] T013 [US2] Add preview endpoint in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs`

## Phase 5: User Story 3 - Deploy And Track Status (Priority: P2)

- [ ] T014 [US3] Add deployment run status tests in `tests/ElsaControl.Api.Tests/WorkspaceDeploymentTargetApiTests.cs`
- [ ] T015 [US3] Implement deployment run service in `src/ElsaControl.PackageCatalog.Core/DeploymentTargets/DeploymentRunService.cs`
- [ ] T016 [US3] Add deployment run endpoints in `src/ElsaControl.Api/Workspace/WorkspaceDeploymentTargetEndpoints.cs`

## Phase 6: Polish

- [ ] T017 Run `dotnet build ElsaControl.sln --no-restore` against `ElsaControl.sln`
- [ ] T018 Run `dotnet test ElsaControl.sln --no-build` against `ElsaControl.sln`
