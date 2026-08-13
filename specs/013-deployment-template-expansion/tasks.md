# Tasks: Deployment Template Expansion

**Input**: Design documents from `/specs/013-deployment-template-expansion/`

## Phase 1: Setup

- [X] T001 [P] Create template target tests in `tests/ValenceControl.PackageCatalog.Core.Tests/DeploymentTemplateTargetTests.cs`
- [X] T002 [P] Create template API tests in `tests/ValenceControl.Api.Tests/DeploymentTemplateBundleApiTests.cs`

## Phase 2: Foundation

- [X] T003 Define target models in `src/ValenceControl.PackageCatalog.Core/DeploymentTemplates/DeploymentTemplateModels.cs`
- [X] T004 Define target renderer registry in `src/ValenceControl.PackageCatalog.Core/DeploymentTemplates/DeploymentTemplateRegistry.cs`
- [X] T005 Extend bundle request target DTO in `src/ValenceControl.Api/Public/Builder/BuilderContracts.cs`

## Phase 3: User Story 1 - Choose Target (Priority: P1)

- [X] T006 [US1] Add tests for default Docker Compose target in `tests/ValenceControl.PackageCatalog.Core.Tests/DeploymentTemplateTargetTests.cs`
- [X] T007 [US1] Route bundle generation through target registry in `src/ValenceControl.PackageCatalog.Core/Builder/BundleGenerationService.cs`

## Phase 4: User Story 2 - Azure Container Apps (Priority: P2)

- [X] T008 [US2] Add Azure template renderer tests in `tests/ValenceControl.PackageCatalog.Core.Tests/DeploymentTemplateTargetTests.cs`
- [X] T009 [US2] Implement Azure Container Apps renderer in `src/ValenceControl.PackageCatalog.Core/DeploymentTemplates/AzureContainerAppsTemplateRenderer.cs`

## Phase 5: User Story 3 - Kubernetes/Helm (Priority: P3)

- [X] T010 [US3] Add Kubernetes/Helm renderer tests in `tests/ValenceControl.PackageCatalog.Core.Tests/DeploymentTemplateTargetTests.cs`
- [X] T011 [US3] Implement Kubernetes/Helm renderer in `src/ValenceControl.PackageCatalog.Core/DeploymentTemplates/KubernetesHelmTemplateRenderer.cs`

## Phase 6: Polish

- [X] T012 Update quickstart examples in `specs/013-deployment-template-expansion/quickstart.md`
- [X] T013 Run `dotnet build ValenceControl.sln --no-restore` against `ValenceControl.sln`
- [X] T014 Run `dotnet test ValenceControl.sln --no-build` against `ValenceControl.sln`
