# Tasks: Generator Adoption Fixes for Elsa Shell Modules

**Input**: Design documents from `/specs/003-generator-adoption-fixes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required by FR-021 through FR-024 and are listed before implementation tasks for each user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared test helpers and inspect existing generator wiring without changing behavior.

- [X] T001 Verify current generator projects and tests build with `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests.csproj` and `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests.csproj`
- [X] T002 [P] Add multi-target project configuration support to `tests/Elsa.Specifications.PackageManifest.Generator.Testing/SampleProjectBuilder.cs`
- [X] T003 [P] Add package path and package entry assertion helpers to `tests/Elsa.Specifications.PackageManifest.Generator.Testing/NuGetPackageInspector.cs`
- [X] T004 [P] Add reusable delegate-shaped shell-feature fixture source snippets to `tests/Elsa.Specifications.PackageManifest.Generator.Testing/CShellsFeatureFixtures.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared diagnostic and type-shape primitives required by all adoption fixes.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Extend generator diagnostic metadata with category/fatal/mappable-validation information in `src/Elsa.Specifications.PackageManifest.Generator.Core/Validation/GenerationDiagnostics.cs`
- [X] T006 Update diagnostic formatting to preserve stable messages for new diagnostic metadata in `src/Elsa.Specifications.PackageManifest.Generator.Core/Validation/GenerationDiagnosticFormatter.cs`
- [X] T007 Add delegate/container type-shape inspection helpers in `src/Elsa.Specifications.PackageManifest.Generator.Core/AssemblyInspection/TypeMetadataHelpers.cs`
- [X] T008 Add unit tests for delegate/container type-shape helpers in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/SettingSchemaGeneratorTests.cs`
- [X] T009 Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests.csproj --filter SettingSchemaGeneratorTests`

**Checkpoint**: Shared diagnostic metadata and delegate type-shape detection are ready.

---

## Phase 3: User Story 1 - Adopt the Generator Without Custom Project Workarounds (Priority: P1) - MVP

**Goal**: A multi-target Elsa shell-feature module can build and pack with only the private generator package reference and exactly one root manifest.

**Independent Test**: Build and pack a representative multi-target sample project, inspect the `.nupkg`, and verify exactly one root `elsa-package.json` from the first declared target framework.

### Tests for User Story 1

- [X] T010 [US1] Add integration test for multi-target pack root manifest inclusion in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/MultiTargetingPackageInspectionTests.cs`
- [X] T011 [US1] Add integration test for direct `dotnet pack` without prior explicit build in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/MultiTargetingPackageInspectionTests.cs`
- [X] T012 [US1] Add integration test for custom `ElsaPackageManifestPackagePath` one-entry behavior in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/MultiTargetingPackageInspectionTests.cs`
- [X] T013 [P] [US1] Add integration test proving the first declared target framework is canonical for equivalent surfaces in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs`
- [X] T014 [P] [US1] Add integration test for divergent target-framework surface diagnostics and configured severity behavior in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs`

### Implementation for User Story 1

- [X] T015 [US1] Wire canonical first-target-framework package inclusion without consumer targets in `src/Elsa.Specifications.PackageManifest.Generator/build/Elsa.Specifications.PackageManifest.Generator.targets`
- [X] T016 [US1] Update multi-target canonical selection to preserve first declared target framework ordering in `src/Elsa.Specifications.PackageManifest.Generator.Core/Generation/MultiTargetManifestCoordinator.cs`
- [X] T017 [US1] Ensure package inclusion diagnostics report canonical manifest source and package path in `src/Elsa.Specifications.PackageManifest.Generator/build/Elsa.Specifications.PackageManifest.Generator.targets`
- [X] T018 [US1] Wire divergent target-framework surface diagnostics through configured severity policy in `src/Elsa.Specifications.PackageManifest.Generator.Core/Generation/MultiTargetManifestCoordinator.cs`
- [X] T019 [US1] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests.csproj --filter MultiTargeting`

**Checkpoint**: User Story 1 is independently functional and demoable with `dotnet pack`.

---

## Phase 4: User Story 2 - Treat Non-Manifestable Properties as Non-Configurable (Priority: P1)

**Goal**: Delegate-shaped shell-feature hooks and unsupported CLR-only setting candidates are ignored without unsupported-setting failures or default warnings.

**Independent Test**: Generate a manifest for a feature with normal settings plus direct delegates, delegate-valued collections/dictionaries, and unsupported non-delegate settings such as `System.Type`, then verify only normal settings appear.

### Tests for User Story 2

- [X] T020 [US2] Add core test for direct `Action<T>` and `Func<IServiceProvider,T>` hook exclusion in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs`
- [X] T021 [US2] Add core test for `Action<IServiceProvider,HttpClient>` hook exclusion in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs`
- [X] T022 [US2] Add core test for delegate-valued dictionary/list hook exclusion in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs`
- [X] T023 [US2] Add no-code-execution safety test for ignored delegates, factories, constructors, and property getters in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/MetadataInspectionSafetyTests.cs`
- [X] T024 [US2] Add core test that ignored delegate hooks do not emit warning diagnostics in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/UnsupportedSettingTypeTests.cs`
- [X] T025 [US2] Add core test that ignored delegate hooks can emit low-importance or verbose diagnostics when verbose diagnostics are enabled in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/UnsupportedSettingTypeTests.cs`
- [X] T026 [US2] Add core regression test that non-delegate complex object settings are omitted with low-importance diagnostics in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/UnsupportedSettingTypeTests.cs`

### Implementation for User Story 2

- [X] T027 [US2] Filter delegate-shaped direct and nested container properties before schema generation in `src/Elsa.Specifications.PackageManifest.Generator.Core/Generation/SettingDiscoveryService.cs`
- [X] T028 [US2] Omit non-delegate unsupported setting candidates before manifest generation in `src/Elsa.Specifications.PackageManifest.Generator.Core/Generation/SettingDiscoveryService.cs`
- [X] T029 [US2] Add verbose-only ignored code hook diagnostics in `src/Elsa.Specifications.PackageManifest.Generator.Core/Validation/GenerationDiagnostics.cs`
- [X] T030 [US2] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests.csproj --filter \"SettingDiscoveryTests|UnsupportedSettingTypeTests|MetadataInspectionSafetyTests\"`

**Checkpoint**: User Story 2 is independently functional and normal deploy-time settings still appear.

---

## Phase 5: User Story 3 - Warning Severity Does Not Fail the Build Unless Requested (Priority: P1)

**Goal**: Warning severity maps manifest validation findings to warnings and task success follows the logged severity and fail-on-warnings policy.

**Independent Test**: Execute the MSBuild task with warning severity and compare task results for fail-on-warnings false versus true.

### Tests for User Story 3

- [X] T031 [US3] Add MSBuild task test where warning severity plus fail-on-warnings false returns success after mapped validation warnings in `tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs`
- [X] T032 [US3] Add MSBuild task test where warning severity plus fail-on-warnings true returns failure after mapped validation warnings in `tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs`
- [X] T033 [P] [US3] Add validation policy unit tests for post-mapping failure behavior in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/GenerationDiagnosticSeverityTests.cs`
- [X] T034 [P] [US3] Add task-level regression test proving warning-only execution succeeds instead of creating an `MSB4181` precursor in `tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs`

### Implementation for User Story 3

- [X] T035 [US3] Update validation severity policy to fail based on mapped diagnostics plus fatal diagnostics in `src/Elsa.Specifications.PackageManifest.Generator.Core/Validation/ValidationSeverityPolicy.cs`
- [X] T036 [US3] Update MSBuild task result logic to avoid returning false after logging warnings only in `src/Elsa.Specifications.PackageManifest.Generator.MSBuild/GenerateElsaPackageManifestTask.cs`
- [X] T037 [US3] Ensure manifest validation findings are marked mappable before policy evaluation in `src/Elsa.Specifications.PackageManifest.Generator.Core/Generation/ManifestGenerator.cs`
- [X] T038 [US3] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests.csproj --filter GenerateElsaPackageManifestTaskDiagnosticTests`

**Checkpoint**: User Story 3 is independently functional and fixes the warning-severity task return regression.

---

## Phase 6: User Story 4 - Preserve Required Manifest Quality Gates (Priority: P2)

**Goal**: Default/error policy still fails required manifest errors, and warning severity does not hide infrastructure or invalid input failures.

**Independent Test**: Generate required validation failures and invalid input failures under default and warning policies and verify pass/fail behavior.

### Tests for User Story 4

- [X] T039 [P] [US4] Add core test proving default policy fails required manifest validation errors in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/GeneratedManifestValidationTests.cs`
- [X] T040 [P] [US4] Add core test proving warning severity does not downgrade invalid override input failures in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/ManifestOverrideValidationTests.cs`
- [X] T041 [P] [US4] Add MSBuild task test proving infrastructure failures fail under warning severity in `tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs`
- [X] T042 [P] [US4] Add integration test proving non-delegate unsupported settings do not fail under default policy in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/ValidationSeverityBuildTests.cs`

### Implementation for User Story 4

- [X] T043 [US4] Mark override parsing and invalid input diagnostics as fatal in `src/Elsa.Specifications.PackageManifest.Generator.Core/Overrides/ManifestOverrideReader.cs`
- [X] T044 [US4] Mark infrastructure exceptions as fatal before logging in `src/Elsa.Specifications.PackageManifest.Generator.MSBuild/GenerateElsaPackageManifestTask.cs`
- [X] T045 [US4] Preserve default required schema error behavior in `src/Elsa.Specifications.PackageManifest.Generator.Core/Validation/GeneratedManifestValidator.cs`
- [X] T046 [US4] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests.csproj --filter \"GeneratedManifestValidationTests|ManifestOverrideValidationTests\"`

**Checkpoint**: User Story 4 preserves manifest quality gates while allowing warning-based adoption.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and full verification across all stories.

- [X] T047 [P] Update package README adoption guidance in `src/Elsa.Specifications.PackageManifest.Generator/README.md`
- [X] T048 [P] Update generator MSBuild/package layout docs in `specs/002-package-manifest-generator/contracts/msbuild-contract.md`
- [X] T049 [P] Update package layout docs for canonical first-target-framework behavior in `specs/002-package-manifest-generator/contracts/package-layout.md`
- [X] T050 Add lightweight type-shape performance regression coverage for representative modules in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/SettingSchemaGeneratorTests.cs`
- [X] T051 Refactor duplicated diagnostic policy setup in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/GenerationDiagnosticSeverityTests.cs`
- [X] T052 Run quickstart validation commands from `specs/003-generator-adoption-fixes/quickstart.md`
- [X] T053 Run full regression suite with `dotnet test ElsaControl.sln`
- [X] T054 Review new abstractions and dependencies against constitution simplicity rules in `specs/003-generator-adoption-fixes/plan.md`

---

## Phase 8: Amendment - Unsupported CLR-Only Setting Omission

**Purpose**: Align the generator adoption policy with real CShells modules that expose unsupported non-delegate CLR-only properties such as provider `System.Type` references.

- [X] T055 [US2] Add core regression coverage for `System.Type` setting omission in `tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/UnsupportedSettingTypeTests.cs`
- [X] T056 [US2] Add MSBuild task regression coverage proving omitted unsupported settings do not warn or fail when fail-on-warnings is enabled in `tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs`
- [X] T057 [US2] Update setting discovery, diagnostic policy, data model, and research contracts in `specs/003-generator-adoption-fixes/`
- [X] T058 [US2] Update project workflow guidance in `AGENTS.md` to require Spec Kit alignment for major features and follow-up adjustments
- [X] T059 [US2] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests.csproj --filter "UnsupportedSettingTypeTests|FeatureDiscoveryTests"`
- [X] T060 [US2] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests.csproj --filter GenerateElsaPackageManifestTaskDiagnosticTests`
- [X] T061 [US2] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests.csproj --filter ValidationSeverityBuildTests`
- [X] T062 [US2] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests.csproj`

---

## Phase 9: Amendment - Build-Then-Pack No-Build Reuses Existing Manifest

**Purpose**: Fix CI pipelines that build successfully and then run `dotnet pack --no-build`, where pack-time reference resolution may be incomplete and manifest generation should not rerun when the build already produced the manifest.

- [X] T063 [US1] Add package-shaped generator fixture support with local targets/task assets and external CShells reference assembly coverage in `tests/Elsa.Specifications.PackageManifest.Generator.Testing/SampleProjectBuilder.cs`
- [X] T064 [US1] Add regression test for `dotnet build --configuration Release` followed by `dotnet pack --configuration Release --no-build` in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/PackTargetBehaviorTests.cs`
- [X] T065 [US1] Add regression test for clean `dotnet pack` generating and including the manifest in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/PackTargetBehaviorTests.cs`
- [X] T066 [US1] Add regression test for clear missing-manifest failure during `dotnet pack --no-build` in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/PackTargetBehaviorTests.cs`
- [X] T067 [US1] Preserve multi-target one-root-manifest coverage in `tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/PackTargetBehaviorTests.cs`
- [X] T068 [US1] Update package target flow in `src/Elsa.Specifications.PackageManifest.Generator/build/Elsa.Specifications.PackageManifest.Generator.targets` so no-build pack reads the existing manifest instead of depending on generation.
- [X] T069 [US1] Update package inclusion contracts, quickstart, and README docs for build-then-pack no-build behavior.
- [X] T070 [US1] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests.csproj --filter PackTargetBehaviorTests`
- [X] T071 [US1] Run `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests.csproj`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion - blocks user stories.
- **User Stories (Phase 3+)**: Depend on Foundational completion. US1, US2, and US3 are all P1 and can proceed in parallel after foundation if file ownership is coordinated.
- **User Story 4 (Phase 6)**: Depends on the diagnostic policy work from US3 and validates default/fatal behavior.
- **Polish (Phase 7)**: Depends on selected user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2; no dependency on US2 or US3.
- **US2 (P1)**: Can start after Phase 2; no dependency on US1 or US3.
- **US3 (P1)**: Can start after Phase 2; no dependency on US1 or US2.
- **US4 (P2)**: Should start after US3 policy behavior is implemented.

### Parallel Opportunities

- T002, T003, and T004 can run in parallel after T001.
- T013 and T014 can be written in parallel with T010 through T012 because they target a different integration test file.
- T023 can be written in parallel with T020 through T022 because it targets a separate safety test file.
- T024 through T026 can be written after T020 through T023 because they validate related diagnostics.
- T033 and T034 can be written in parallel with T031 through T032 because they target different test projects.
- T039 through T042 can be written in parallel before US4 implementation.
- T047, T048, and T049 can run in parallel during polish.

## Parallel Example: User Story 2

```bash
Task: "Add core test for direct Action<T> and Func<IServiceProvider,T> hook exclusion in tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs"
Task: "Add core test that ignored delegate hooks do not emit warning diagnostics in tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/UnsupportedSettingTypeTests.cs"
```

## Parallel Example: User Story 3

```bash
Task: "Add MSBuild task test where warning severity plus fail-on-warnings false returns success after mapped validation warnings in tests/Elsa.Specifications.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs"
Task: "Add validation policy unit tests for post-mapping failure behavior in tests/Elsa.Specifications.PackageManifest.Generator.Core.Tests/GenerationDiagnosticSeverityTests.cs"
Task: "Add integration test proving warning-only builds do not emit MSB4181 in tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/ValidationSeverityBuildTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 for multi-target package inclusion.
3. Validate with `dotnet test tests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests/Elsa.Specifications.PackageManifest.Generator.IntegrationTests.csproj --filter MultiTargeting`.
4. Inspect a produced `.nupkg` and confirm exactly one root `elsa-package.json`.

### Adoption-Hardening Sequence

1. Complete US1 to remove consumer-side pack workarounds.
2. Complete US2 to stop delegate-shaped code hooks from blocking normal builds.
3. Complete US3 to fix warning severity task return behavior.
4. Complete US4 to preserve default required validation failures and fatal input failures.
5. Run polish and full regression.

### Notes

- Tests should be added before implementation and should fail for the current regression behavior.
- Avoid new dependencies and new projects; keep changes inside existing generator core, MSBuild, package facade, and test projects.
- Do not execute consumer package code, constructors, property getters, delegates, or factories while implementing these tasks.
