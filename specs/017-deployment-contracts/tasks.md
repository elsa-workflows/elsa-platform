# Tasks: Deployment Foundation Contracts

**Input**: Design documents from `specs/017-deployment-contracts/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`

**Tests**: Test tasks are included because this slice defines public foundation contracts and architectural boundaries.

**Progress Tracking**: This file is the source of truth. Check tasks as they complete and add blocker notes directly under blocked tasks.

## Phase 1: Setup

**Purpose**: Add the deployment foundation project skeletons to the solution.

- [x] T001 Create deployment abstractions project in `src/Elsa.Platform.Deployment.Abstractions/Elsa.Platform.Deployment.Abstractions.csproj`
- [x] T002 Create deployment abstractions test project in `tests/Elsa.Platform.Deployment.Abstractions.Tests/Elsa.Platform.Deployment.Abstractions.Tests.csproj`
- [x] T003 Add deployment abstractions source and test projects to `Elsa.Platform.sln`
- [x] T004 Create source folders in `src/Elsa.Platform.Deployment.Abstractions/Artifacts`, `Diagnostics`, `History`, `Plans`, `Resources`, and `Targets`

**Checkpoint**: Empty project skeletons restore and build.

---

## Phase 2: Foundational Contract Scaffolding

**Purpose**: Establish shared enums and primitive value types used by every user story.

- [x] T005 [P] Create deployment diagnostic severity and status enums in `src/Elsa.Platform.Deployment.Abstractions/Diagnostics/DeploymentDiagnosticSeverity.cs`
- [x] T006 [P] Create deployment operation mode enum in `src/Elsa.Platform.Deployment.Abstractions/DeploymentOperationMode.cs`
- [x] T007 [P] Create deployment status enum in `src/Elsa.Platform.Deployment.Abstractions/DeploymentStatus.cs`
- [x] T008 [P] Create deployment change action and status enums in `src/Elsa.Platform.Deployment.Abstractions/Plans/DeploymentChangeAction.cs` and `src/Elsa.Platform.Deployment.Abstractions/Plans/DeploymentChangeStatus.cs`
- [x] T009 [P] Create deletion behavior enum in `src/Elsa.Platform.Deployment.Abstractions/Resources/DeploymentDeletionBehavior.cs`
- [x] T010 [P] Create digest value object in `src/Elsa.Platform.Deployment.Abstractions/Artifacts/ArtifactDigest.cs`

**Checkpoint**: Shared primitives compile before higher-level contracts are added.

---

## Phase 3: User Story 1 - Describe Deployable Resources (Priority: P1)

**Goal**: Model resource, artifact, plan, result, and history data needed by the Phase 1 deployment loop.

**Independent Test**: Representative workflow and variable resources can be placed into artifact, plan, result, and history records with required identity, status, diagnostic, and digest data.

### Tests

- [x] T011 [P] [US1] Add resource identity tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/ResourceIdentityTests.cs`
- [x] T012 [P] [US1] Add artifact contract tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/ArtifactContractTests.cs`
- [x] T013 [P] [US1] Add diagnostic contract tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/DiagnosticContractTests.cs`
- [x] T014 [P] [US1] Add plan contract tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/PlanContractTests.cs`
- [x] T015 [P] [US1] Add history contract tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/HistoryContractTests.cs`

### Implementation

- [x] T016 [US1] Implement resource identity and resource contracts in `src/Elsa.Platform.Deployment.Abstractions/Resources/DeploymentResourceId.cs` and `src/Elsa.Platform.Deployment.Abstractions/Resources/DeploymentResource.cs`
- [x] T017 [US1] Implement artifact identity and metadata contracts in `src/Elsa.Platform.Deployment.Abstractions/Artifacts/DeploymentArtifactIdentity.cs` and `src/Elsa.Platform.Deployment.Abstractions/Artifacts/DeploymentArtifactMetadata.cs`
- [x] T018 [US1] Implement target descriptor contract in `src/Elsa.Platform.Deployment.Abstractions/Targets/DeploymentTargetDescriptor.cs`
- [x] T019 [US1] Implement diagnostic contract in `src/Elsa.Platform.Deployment.Abstractions/Diagnostics/DeploymentDiagnostic.cs`
- [x] T020 [US1] Implement plan and change contracts in `src/Elsa.Platform.Deployment.Abstractions/Plans/DeploymentPlan.cs` and `src/Elsa.Platform.Deployment.Abstractions/Plans/DeploymentChange.cs`
- [x] T021 [US1] Implement result and resource operation result contracts in `src/Elsa.Platform.Deployment.Abstractions/DeploymentResult.cs` and `src/Elsa.Platform.Deployment.Abstractions/DeploymentResourceResult.cs`
- [x] T022 [US1] Implement history record and actor contracts in `src/Elsa.Platform.Deployment.Abstractions/History/DeploymentHistoryRecord.cs` and `src/Elsa.Platform.Deployment.Abstractions/History/DeploymentActor.cs`

**Checkpoint**: Core contract tests pass.

---

## Phase 4: User Story 2 - Enforce Platform Boundaries (Priority: P2)

**Goal**: Prove deployment foundation contracts do not depend on forbidden implementation packages or model runtime execution state.

**Independent Test**: Boundary tests inspect project references and source vocabulary.

### Tests

- [ ] T023 [P] [US2] Add dependency boundary tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/DependencyBoundaryTests.cs`

### Implementation

- [ ] T024 [US2] Verify deployment abstractions project has no forbidden project references in `src/Elsa.Platform.Deployment.Abstractions/Elsa.Platform.Deployment.Abstractions.csproj`
- [ ] T025 [US2] Keep forbidden runtime-state vocabulary out of public source files under `src/Elsa.Platform.Deployment.Abstractions/`

**Checkpoint**: Boundary tests pass.

---

## Phase 5: User Story 3 - Prepare Extension Points (Priority: P3)

**Goal**: Provide minimal extension interfaces for future handlers, artifact IO, validation, engine entry points, targets, and history stores.

**Independent Test**: Tests compile sample implementations for every extension point without infrastructure dependencies.

### Tests

- [ ] T026 [P] [US3] Add sample extension implementation tests in `tests/Elsa.Platform.Deployment.Abstractions.Tests/ExtensionContractTests.cs`

### Implementation

- [ ] T027 [US3] Implement resource handler and state reader contracts in `src/Elsa.Platform.Deployment.Abstractions/Resources/IResourceHandler.cs` and `src/Elsa.Platform.Deployment.Abstractions/Resources/IResourceStateReader.cs`
- [ ] T028 [US3] Implement resource validator contract in `src/Elsa.Platform.Deployment.Abstractions/Resources/IResourceValidator.cs`
- [ ] T029 [US3] Implement artifact reader and writer contracts in `src/Elsa.Platform.Deployment.Abstractions/Artifacts/IArtifactReader.cs` and `src/Elsa.Platform.Deployment.Abstractions/Artifacts/IArtifactWriter.cs`
- [ ] T030 [US3] Implement deployment target contract in `src/Elsa.Platform.Deployment.Abstractions/Targets/IDeploymentTarget.cs`
- [ ] T031 [US3] Implement history store contract in `src/Elsa.Platform.Deployment.Abstractions/History/IDeploymentHistoryStore.cs`
- [ ] T032 [US3] Implement deployment engine entry point contract in `src/Elsa.Platform.Deployment.Abstractions/IDeploymentEngine.cs`

**Checkpoint**: Extension contract tests pass.

---

## Final Phase: Polish And Verification

- [ ] T033 Update deployment roadmap slice status in `docs/deployment-platform-phased-strategy.md`
- [ ] T034 Update quickstart verification notes in `specs/017-deployment-contracts/quickstart.md`
- [ ] T035 Run focused deployment abstractions tests with `dotnet test tests/Elsa.Platform.Deployment.Abstractions.Tests/Elsa.Platform.Deployment.Abstractions.Tests.csproj`
- [ ] T036 Run full solution tests with `dotnet test Elsa.Platform.sln`
- [ ] T037 Run `git diff --check`
- [ ] T038 Confirm `tasks.md` checkboxes and blocker notes reflect actual progress

---

## Dependencies & Execution Order

- Phase 1 setup blocks every implementation task.
- Phase 2 primitives block Phase 3 core model implementation.
- User Story 1 is the MVP and must complete before boundary and extension claims are meaningful.
- User Story 2 can run after project skeletons exist and should complete before final verification.
- User Story 3 depends on the core model contracts from User Story 1.

## Parallel Opportunities

- T005 through T010 can be implemented in parallel after project skeletons exist.
- T011 through T015 can be written in parallel because they target separate test files.
- T023 and T026 can be written in parallel after core contracts are available.

## MVP First

The MVP for this slice is Phase 1 through Phase 3:

1. Add the abstractions project and tests to the solution.
2. Define the shared primitive enums and digest value.
3. Implement tested resource, artifact, diagnostic, plan, result, target, and history contracts.

Stop and validate after the MVP before expanding into extension interfaces and boundary checks.
