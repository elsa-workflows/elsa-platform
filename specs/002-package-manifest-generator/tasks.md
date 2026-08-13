# Tasks: Elsa Package Manifest Generator

**Input**: Design documents from `/specs/002-package-manifest-generator/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are included because the specification and quickstart require safety, validation, determinism, MSBuild, pack, and package inspection verification.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add generator projects, packages, and test project shells.

- [X] T001 Add `ValenceControl.PackageManifest.Generator`, `ValenceControl.PackageManifest.Generator.Core`, and `ValenceControl.PackageManifest.Generator.MSBuild` projects to `ValenceControl.sln`
- [X] T002 Create `src/ValenceControl.PackageManifest.Generator/ValenceControl.PackageManifest.Generator.csproj` configured as the package facade for build assets and optional source-only manifest hints
- [X] T003 Create `src/ValenceControl.PackageManifest.Generator.Core/ValenceControl.PackageManifest.Generator.Core.csproj` with references to `ValenceControl.PackageManifests`, `JsonSchema.Net`, and `NuGet.Versioning`
- [X] T004 Create `src/ValenceControl.PackageManifest.Generator.MSBuild/ValenceControl.PackageManifest.Generator.MSBuild.csproj` with MSBuild task dependencies and a project reference to `ValenceControl.PackageManifest.Generator.Core`
- [X] T005 [P] Create `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ValenceControl.PackageManifest.Generator.Core.Tests.csproj` with xUnit references; use xUnit's built-in assertions.
- [X] T006 [P] Create `tests/ValenceControl.PackageManifest.Generator.MSBuild.Tests/ValenceControl.PackageManifest.Generator.MSBuild.Tests.csproj` with xUnit references; use xUnit's built-in assertions.
- [X] T007 [P] Create `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/ValenceControl.PackageManifest.Generator.IntegrationTests.csproj` with xUnit and fixture-support references; use xUnit's built-in assertions.
- [X] T008 [P] Create `tests/ValenceControl.PackageManifest.Generator.Testing/ValenceControl.PackageManifest.Generator.Testing.csproj` for shared sample-project and package-inspection helpers
- [X] T009 Add central package versions for required generator test/build dependencies in `Directory.Packages.props`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared contracts, CShells metadata inspection, optional manifest hints, core value objects, and test fixtures required by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T010 Define generator option, metadata, feature, setting, diagnostic, and artifact value objects in `src/ValenceControl.PackageManifest.Generator.Core/Generation/GeneratorModels.cs`
- [X] T011 Implement diagnostic severity and diagnostic collection types in `src/ValenceControl.PackageManifest.Generator.Core/Validation/GenerationDiagnostics.cs`
- [X] T012 Implement deterministic JSON serialization helpers for generated manifests in `src/ValenceControl.PackageManifest.Generator.Core/Generation/DeterministicJsonSerializer.cs`
- [X] T013 Add CShells reference metadata fixtures for `IShellFeature` and `ShellFeatureAttribute` inspection in `tests/ValenceControl.PackageManifest.Generator.Testing/CShellsFeatureFixtures.cs`
- [X] T014 Implement optional source-only `ManifestSettingAttribute` in `src/ValenceControl.PackageManifest.Generator/src/ValenceControl.PackageManifest.Generator.Hints/ManifestSettingAttribute.cs`
- [X] T015 [P] Implement optional source-only `ManifestIgnoreAttribute` in `src/ValenceControl.PackageManifest.Generator/src/ValenceControl.PackageManifest.Generator.Hints/ManifestIgnoreAttribute.cs`
- [X] T016 [P] Implement optional source-only `ManifestExtensionAttribute` in `src/ValenceControl.PackageManifest.Generator/src/ValenceControl.PackageManifest.Generator.Hints/ManifestExtensionAttribute.cs`
- [X] T017 Configure optional source-only manifest hint packaging in `src/ValenceControl.PackageManifest.Generator/ValenceControl.PackageManifest.Generator.csproj`
- [X] T018 Add CShells package/reference metadata handling to generator project setup in `Directory.Packages.props`
- [X] T019 Add build props template in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.props`
- [X] T020 Add build targets template in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.targets`
- [X] T021 Add buildTransitive props and targets forwarding files in `src/ValenceControl.PackageManifest.Generator/buildTransitive/ValenceControl.PackageManifest.Generator.props` and `src/ValenceControl.PackageManifest.Generator/buildTransitive/ValenceControl.PackageManifest.Generator.targets`
- [X] T022 [P] Implement reusable sample project builder in `tests/ValenceControl.PackageManifest.Generator.Testing/SampleProjectBuilder.cs`
- [X] T023 [P] Implement NuGet package inspection helper in `tests/ValenceControl.PackageManifest.Generator.Testing/NuGetPackageInspector.cs`
- [X] T024 [P] Implement constructor/property getter tripwire fixture types in `tests/ValenceControl.PackageManifest.Generator.Testing/TripwireFeatureFixtures.cs`
- [X] T025 Verify foundational project layout builds by running `dotnet build`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Generate Manifest Automatically (Priority: P1) - MVP

**Goal**: A package author adds one private package reference and gets `elsa-package.json` generated during build/pack and included at the NuGet package root.

**Independent Test**: Build and pack a sample class library project referencing the generator package, then verify the intermediate manifest exists and the `.nupkg` contains one root `elsa-package.json`.

### Tests for User Story 1

- [ ] T026 [P] [US1] Add integration test for build-time intermediate manifest generation in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/BuildGeneratesManifestTests.cs`
- [ ] T027 [P] [US1] Add integration test for direct `dotnet pack` root manifest inclusion in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/PackIncludesManifestTests.cs`
- [ ] T028 [P] [US1] Add integration test for `GenerateElsaPackageManifest=false` behavior in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/GenerationDisableTests.cs`
- [X] T029 [P] [US1] Add unit tests for project/package metadata mapping in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ProjectPackageMetadataTests.cs`

### Implementation for User Story 1

- [X] T030 [US1] Implement MSBuild task parameter model in `src/ValenceControl.PackageManifest.Generator.MSBuild/GenerateElsaPackageManifestTask.cs`
- [X] T031 [US1] Implement MSBuild property resolution in `src/ValenceControl.PackageManifest.Generator.MSBuild/Packaging/MsBuildGeneratorOptionsMapper.cs`
- [X] T032 [US1] Implement project and NuGet metadata mapper in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ProjectPackageMetadataMapper.cs`
- [X] T033 [US1] Implement minimal manifest generation orchestration in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestGenerator.cs`
- [X] T034 [US1] Wire task execution to `ManifestGenerator` and deterministic file writing in `src/ValenceControl.PackageManifest.Generator.MSBuild/GenerateElsaPackageManifestTask.cs`
- [X] T035 [US1] Wire targets to run after compile and before pack in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.targets`
- [X] T036 [US1] Wire generated file package inclusion at root `elsa-package.json` in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.targets`
- [X] T037 [US1] Add task assembly and build asset packing rules in `src/ValenceControl.PackageManifest.Generator/ValenceControl.PackageManifest.Generator.csproj`
- [X] T038 [US1] Ensure generated manifest uses `ValenceControl.PackageManifests` DTOs in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestGenerator.cs`
- [X] T039 [US1] Run `dotnet test --filter ValenceControl.PackageManifest.Generator.IntegrationTests` and fix US1 failures

**Checkpoint**: User Story 1 is independently functional and demoable.

---

## Phase 4: User Story 2 - Discover Features and Settings (Priority: P1)

**Goal**: Discover CShells feature classes and public configurable settings without duplicate JSON declarations.

**Independent Test**: Build a sample package containing CShells `IShellFeature` classes with `ShellFeatureAttribute` metadata and verify feature/settings metadata, schema type mapping, exclusions, and no runtime code execution.

### Tests for User Story 2

- [X] T040 [P] [US2] Add unit tests for metadata-only feature discovery by direct `IShellFeature` implementation in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/FeatureDiscoveryTests.cs`
- [ ] T041 [P] [US2] Add unit tests for metadata-only feature discovery through a base type implementing `IShellFeature` in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/FeatureDiscoveryTests.cs`
- [ ] T042 [P] [US2] Add unit tests for `ShellFeatureAttribute` name, display name, description, dependency, and metadata enrichment in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ShellFeatureMetadataTests.cs`
- [X] T043 [P] [US2] Add unit tests for setting inclusion and exclusions in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs`
- [X] T044 [P] [US2] Add unit tests for supported type-to-schema mapping in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/SettingSchemaGeneratorTests.cs`
- [ ] T045 [P] [US2] Add unit tests for unsupported complex object omission diagnostics in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/UnsupportedSettingTypeTests.cs`
- [X] T046 [P] [US2] Add safety test proving constructors and property getters are not invoked in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/MetadataInspectionSafetyTests.cs`

### Implementation for User Story 2

- [X] T047 [US2] Implement metadata-only assembly reader in `src/ValenceControl.PackageManifest.Generator.Core/AssemblyInspection/AssemblyMetadataReader.cs`
- [X] T048 [US2] Implement `IShellFeature` assignability matching and CShells feature-name convention in `src/ValenceControl.PackageManifest.Generator.Core/AssemblyInspection/FeatureTypeMatcher.cs`
- [X] T049 [US2] Implement feature discovery rules in `src/ValenceControl.PackageManifest.Generator.Core/Generation/FeatureDiscoveryService.cs`
- [X] T050 [US2] Implement `ShellFeatureAttribute` and manifest hint metadata reader in `src/ValenceControl.PackageManifest.Generator.Core/AssemblyInspection/FeatureMetadataReader.cs`
- [X] T051 [US2] Implement setting property discovery rules in `src/ValenceControl.PackageManifest.Generator.Core/Generation/SettingDiscoveryService.cs`
- [X] T052 [US2] Implement nullable metadata reader in `src/ValenceControl.PackageManifest.Generator.Core/AssemblyInspection/NullableMetadataReader.cs`
- [X] T053 [US2] Implement common validation annotation mapping in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ValidationAnnotationMapper.cs`
- [ ] T053a [P] [US2] Add unit tests for non-executing default value discovery from supported metadata in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/SettingDefaultValueTests.cs`
- [X] T053b [US2] Implement default value metadata extraction without constructors, property getters, or runtime feature activation in `src/ValenceControl.PackageManifest.Generator.Core/Generation/SettingDefaultValueResolver.cs`
- [X] T053c [US2] Treat non-nullable Boolean settings as optional with a safe `false` default unless explicit required/default metadata is present in `src/ValenceControl.PackageManifest.Generator.Core/Generation/SettingDefaultValueResolver.cs` and `tests/ValenceControl.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs`
- [X] T054 [US2] Implement JSON Schema Draft 2020-12 setting schema generator for primitives, enums, nullable values, arrays/lists, and dictionaries in `src/ValenceControl.PackageManifest.Generator.Core/SchemaGeneration/SettingSchemaGenerator.cs`
- [X] T055 [US2] Implement unsupported complex object omission diagnostics in `src/ValenceControl.PackageManifest.Generator.Core/SchemaGeneration/UnsupportedTypeDiagnosticFactory.cs`
- [X] T056 [US2] Integrate discovered features and settings into manifest generation in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestGenerator.cs`
- [X] T057 [US2] Add MSBuild property for `ElsaPackageManifestAdditionalFeatureInterfaceTypes` in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.props`
- [X] T058 [US2] Run `dotnet test --filter ValenceControl.PackageManifest.Generator.Core.Tests` and fix US2 failures

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Enrich Metadata from Documentation and Overrides (Priority: P2)

**Goal**: Use XML documentation, CShells metadata, optional manifest hints, and `elsa-package.overrides.json` to enrich final manifest metadata with deterministic merge behavior.

**Independent Test**: Build a sample package with XML docs, CShells metadata, optional manifest hints, and override JSON, then verify merge precedence and validation of override references, identity conflicts, and size limits.

### Tests for User Story 3

- [ ] T059 [P] [US3] Add XML documentation extraction tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/XmlDocumentationReaderTests.cs`
- [ ] T060 [P] [US3] Add CShells metadata and manifest hint merge precedence tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/MetadataMergeTests.cs`
- [ ] T061 [P] [US3] Add override schema and parsing tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestOverrideReaderTests.cs`
- [ ] T062 [P] [US3] Add merge precedence tests for inferred, XML, CShells metadata, manifest hint, and override layers in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestMergeTests.cs`
- [X] T063 [P] [US3] Add override invalid reference and identity conflict tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestOverrideValidationTests.cs`
- [ ] T064 [P] [US3] Add override 256 KB size limit test in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestOverrideValidationTests.cs`
- [ ] T064a [P] [US3] Add override merge tests for feature dependencies, conflicts, and required capabilities in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestOverrideCollectionMergeTests.cs`

### Implementation for User Story 3

- [X] T065 [US3] Implement XML documentation reader in `src/ValenceControl.PackageManifest.Generator.Core/Documentation/XmlDocumentationReader.cs`
- [X] T066 [US3] Implement XML documentation application to features and settings in `src/ValenceControl.PackageManifest.Generator.Core/Documentation/XmlDocumentationEnricher.cs`
- [X] T067 [US3] Implement override DTOs in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/ManifestOverrideModels.cs`
- [X] T068 [US3] Embed override file schema from `specs/002-package-manifest-generator/contracts/override-file.schema.json` into `src/ValenceControl.PackageManifest.Generator.Core/Overrides/Schemas/elsa-package.overrides.schema.json`
- [X] T069 [US3] Implement override reader with 256 KB size enforcement in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/ManifestOverrideReader.cs`
- [ ] T070 [US3] Implement override structure validation using JsonSchema.Net in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/ManifestOverrideValidator.cs`
- [X] T071 [US3] Implement deterministic merge service in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestMetadataMerger.cs`
- [X] T072 [US3] Implement override feature/setting reference resolution in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/ManifestOverrideReferenceResolver.cs`
- [ ] T073 [US3] Implement package ID/version conflict validation in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/ManifestOverrideValidator.cs`
- [X] T074 [US3] Integrate XML docs, CShells metadata, manifest hints, and overrides into `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestGenerator.cs`
- [ ] T075 [US3] Run `dotnet test --filter ManifestOverride` and fix US3 failures

**Checkpoint**: User Story 3 enriches manifests without manual full manifest maintenance.

---

## Phase 6: User Story 4 - Validate and Diagnose Build Output (Priority: P2)

**Goal**: Validate final manifests against `ValenceControl.PackageManifests` and emit clear configurable diagnostics.

**Independent Test**: Generate valid and invalid sample manifests and verify default failures, warning behavior, strict mode, fail-on-warnings, unsupported types, and actionable diagnostic content.

### Tests for User Story 4

- [ ] T076 [P] [US4] Add manifest validation success/failure tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/GeneratedManifestValidationTests.cs`
- [ ] T077 [P] [US4] Add diagnostic severity mapping tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/GenerationDiagnosticSeverityTests.cs`
- [ ] T078 [P] [US4] Add strict mode and fail-on-warnings tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/StrictModeValidationTests.cs`
- [ ] T079 [P] [US4] Add generated manifest 1 MB size limit tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/GeneratedManifestSizeTests.cs`
- [ ] T080 [P] [US4] Add MSBuild diagnostic output tests in `tests/ValenceControl.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs`

### Implementation for User Story 4

- [X] T081 [US4] Implement generated manifest validator wrapper around `ValenceControl.PackageManifests` in `src/ValenceControl.PackageManifest.Generator.Core/Validation/GeneratedManifestValidator.cs`
- [X] T082 [US4] Implement recommended metadata validator in `src/ValenceControl.PackageManifest.Generator.Core/Validation/RecommendedMetadataValidator.cs`
- [X] T083 [US4] Implement validation severity policy in `src/ValenceControl.PackageManifest.Generator.Core/Validation/ValidationSeverityPolicy.cs`
- [X] T084 [US4] Implement generated manifest 1 MB size enforcement in `src/ValenceControl.PackageManifest.Generator.Core/Validation/GeneratedManifestSizeValidator.cs`
- [X] T085 [US4] Implement diagnostic formatting with stable codes in `src/ValenceControl.PackageManifest.Generator.Core/Validation/GenerationDiagnosticFormatter.cs`
- [X] T086 [US4] Map generation diagnostics to MSBuild log events in `src/ValenceControl.PackageManifest.Generator.MSBuild/GenerateElsaPackageManifestTask.cs`
- [X] T087 [US4] Add MSBuild properties for strict mode, validation severity, fail-on-warnings, and verbosity in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.props`
- [X] T088 [US4] Ensure secret default values are redacted from diagnostics in `src/ValenceControl.PackageManifest.Generator.Core/Validation/GenerationDiagnosticFormatter.cs`
- [ ] T089 [US4] Run `dotnet test --filter Validation` and fix US4 failures

**Checkpoint**: User Story 4 provides reliable validation and actionable build feedback.

---

## Phase 7: User Story 5 - Handle Multi-Targeting Predictably (Priority: P2)

**Goal**: Multi-targeted package projects produce one canonical manifest by default and warn/fail on divergent feature surfaces.

**Independent Test**: Build and pack multi-target sample projects with identical and divergent feature surfaces, then verify canonical package inclusion and configured severity behavior.

### Tests for User Story 5

- [ ] T090 [P] [US5] Add integration test for identical multi-target feature surfaces in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs`
- [ ] T091 [P] [US5] Add integration test for divergent multi-target feature surfaces failing by default in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs`
- [ ] T092 [P] [US5] Add integration test for explicitly allowed target-framework differences in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs`
- [ ] T093 [P] [US5] Add package inspection test verifying one root manifest for multi-target packages in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingPackageInspectionTests.cs`

### Implementation for User Story 5

- [X] T094 [US5] Implement manifest surface normalization for target framework comparison in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestSurfaceComparer.cs`
- [X] T095 [US5] Implement multi-target canonical manifest selection in `src/ValenceControl.PackageManifest.Generator.Core/Generation/MultiTargetManifestCoordinator.cs`
- [X] T096 [US5] Add target-framework difference diagnostics in `src/ValenceControl.PackageManifest.Generator.Core/Validation/MultiTargetingDiagnostics.cs`
- [ ] T097 [US5] Wire outer-build and inner-build coordination in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.targets`
- [X] T098 [US5] Add `ElsaPackageManifestAllowTargetFrameworkDifferences` handling in `src/ValenceControl.PackageManifest.Generator.MSBuild/Packaging/MsBuildGeneratorOptionsMapper.cs`
- [ ] T099 [US5] Ensure pack item inclusion happens once for multi-target projects in `src/ValenceControl.PackageManifest.Generator/build/ValenceControl.PackageManifest.Generator.targets`
- [ ] T100 [US5] Run multi-target integration tests and fix US5 failures with `dotnet test --filter MultiTargeting`

**Checkpoint**: User Story 5 supports predictable multi-targeted package output.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation, and release-readiness across all stories.

- [X] T101 [P] Update generator README/package notes in `src/ValenceControl.PackageManifest.Generator/README.md`
- [X] T102 [P] Add sample override file documentation in `src/ValenceControl.PackageManifest.Generator/README.md`
- [ ] T103 [P] Add sample fixture project documentation in `tests/ValenceControl.PackageManifest.Generator.IntegrationTests/Fixtures/README.md`
- [X] T104 Run quickstart validation steps from `specs/002-package-manifest-generator/quickstart.md`
- [X] T105 Run full solution tests with `dotnet test`
- [X] T106 Run `dotnet pack` for generator package and inspect package layout against `specs/002-package-manifest-generator/contracts/package-layout.md`
- [ ] T107 Review new abstractions and dependencies against constitution simplicity rules in `specs/002-package-manifest-generator/plan.md`
- [X] T108 Confirm no generator path executes package constructors or property getters by reviewing safety tests in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/MetadataInspectionSafetyTests.cs`
- [ ] T109 Run a performance smoke test proving generation adds no more than 2 seconds for a warm sample project with fewer than 50 feature types and 500 settings

## Phase 9: Follow-up - Setting UI Hints And Options

**Purpose**: Add recommended UI hint metadata for Runtime Builder clients while keeping validation authoritative and generation metadata-only.

- [X] T110 [US2] Amend `specs/002-package-manifest-generator/spec.md`, `data-model.md`, and contracts for setting UI hints, static options, dynamic option providers, and `UI` capitalization.
- [X] T111 [US2] Add source-only `ManifestUIOptionAttribute` and `ManifestUIOptionsProviderAttribute` in `src/ValenceControl.PackageManifest.Generator/src/ValenceControl.PackageManifest.Generator.Hints/`.
- [X] T112 [US2] Emit enum values as setting validation metadata and default enum settings to `select-list` UI options in `src/ValenceControl.PackageManifest.Generator.Core/Generation/SettingDiscoveryService.cs` and `ManifestGenerator.cs`.
- [X] T113 [US3] Extend override model/schema support for structured setting `ui` metadata in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/` and `specs/002-package-manifest-generator/contracts/override-file.schema.json`.
- [X] T114 [US2] Add generator tests for enum-derived UI options and code-first static/dynamic UI option hints in `tests/ValenceControl.PackageManifest.Generator.Core.Tests/FeatureDiscoveryTests.cs`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion; blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational; MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; can proceed in parallel with US1 after shared project wiring exists, but manifest output is easiest to validate after US1.
- **User Story 3 (Phase 5)**: Depends on US2 metadata projections.
- **User Story 4 (Phase 6)**: Depends on US1 and enough US2/US3 data to validate realistic manifests.
- **User Story 5 (Phase 7)**: Depends on US1 and US2.
- **Polish (Phase 8)**: Depends on all desired stories.

### User Story Dependencies

- **US1 Generate Manifest Automatically**: MVP; no dependency on other stories after foundation.
- **US2 Discover Features and Settings**: Independent discovery slice after foundation; integrates into US1 manifest output.
- **US3 Enrich Metadata from Documentation and Overrides**: Requires discovered features/settings from US2.
- **US4 Validate and Diagnose Build Output**: Requires generated manifests from US1 and representative metadata from US2/US3.
- **US5 Handle Multi-Targeting Predictably**: Requires US1 output and US2 feature-surface comparison.

### Parallel Opportunities

- Setup test project creation tasks T005-T008 can run in parallel.
- Foundational manifest hint and testing helper tasks T015-T017 and T022-T024 can run in parallel.
- Test-writing tasks inside each user story are parallelizable.
- US3 override reader/schema work can proceed in parallel with US4 diagnostic policy once US2 projections are stable.
- US5 integration tests and comparison service can be developed in parallel after US1/US2 contracts settle.

---

## Parallel Examples

### User Story 1

```text
Task: "T026 Add integration test for build-time intermediate manifest generation in tests/ValenceControl.PackageManifest.Generator.IntegrationTests/BuildGeneratesManifestTests.cs"
Task: "T027 Add integration test for direct dotnet pack root manifest inclusion in tests/ValenceControl.PackageManifest.Generator.IntegrationTests/PackIncludesManifestTests.cs"
Task: "T028 Add integration test for GenerateElsaPackageManifest=false behavior in tests/ValenceControl.PackageManifest.Generator.IntegrationTests/GenerationDisableTests.cs"
Task: "T029 Add unit tests for project/package metadata mapping in tests/ValenceControl.PackageManifest.Generator.Core.Tests/ProjectPackageMetadataTests.cs"
```

### User Story 2

```text
Task: "T040 Add unit tests for metadata-only feature discovery by direct IShellFeature implementation in tests/ValenceControl.PackageManifest.Generator.Core.Tests/FeatureDiscoveryTests.cs"
Task: "T043 Add unit tests for setting inclusion and exclusions in tests/ValenceControl.PackageManifest.Generator.Core.Tests/SettingDiscoveryTests.cs"
Task: "T044 Add unit tests for supported type-to-schema mapping in tests/ValenceControl.PackageManifest.Generator.Core.Tests/SettingSchemaGeneratorTests.cs"
Task: "T046 Add safety test proving constructors and property getters are not invoked in tests/ValenceControl.PackageManifest.Generator.Core.Tests/MetadataInspectionSafetyTests.cs"
```

### User Story 3

```text
Task: "T059 Add XML documentation extraction tests in tests/ValenceControl.PackageManifest.Generator.Core.Tests/XmlDocumentationReaderTests.cs"
Task: "T061 Add override schema and parsing tests in tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestOverrideReaderTests.cs"
Task: "T063 Add override invalid reference and identity conflict tests in tests/ValenceControl.PackageManifest.Generator.Core.Tests/ManifestOverrideValidationTests.cs"
```

### User Story 4

```text
Task: "T076 Add manifest validation success/failure tests in tests/ValenceControl.PackageManifest.Generator.Core.Tests/GeneratedManifestValidationTests.cs"
Task: "T077 Add diagnostic severity mapping tests in tests/ValenceControl.PackageManifest.Generator.Core.Tests/GenerationDiagnosticSeverityTests.cs"
Task: "T080 Add MSBuild diagnostic output tests in tests/ValenceControl.PackageManifest.Generator.MSBuild.Tests/GenerateElsaPackageManifestTaskDiagnosticTests.cs"
```

### User Story 5

```text
Task: "T090 Add integration test for identical multi-target feature surfaces in tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs"
Task: "T091 Add integration test for divergent multi-target feature surfaces failing by default in tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingManifestTests.cs"
Task: "T093 Add package inspection test verifying one root manifest for multi-target packages in tests/ValenceControl.PackageManifest.Generator.IntegrationTests/MultiTargetingPackageInspectionTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundation.
3. Complete Phase 3 User Story 1.
4. Stop and validate: sample project build creates intermediate manifest and `dotnet pack` includes one root `elsa-package.json`.

### Incremental Delivery

1. Add US1 for build/pack artifact generation.
2. Add US2 for feature and setting discovery.
3. Add US3 for documentation, CShells metadata, manifest hints, and overrides.
4. Add US4 for schema validation and diagnostics.
5. Add US5 for multi-targeting.
6. Run Polish tasks and quickstart validation.

### Team Parallel Strategy

1. Complete Setup and Foundational phases together.
2. Assign US1 MSBuild packaging to one developer.
3. Assign US2 metadata inspection/schema generation to another developer.
4. Assign US3 override/XML merge work after US2 projections are stable.
5. Assign US4 diagnostics and validation once manifest generation is producing representative manifests.
6. Assign US5 multi-targeting once US1/US2 behavior is stable.

## Notes

- [P] tasks use different files and can run in parallel after their phase dependencies.
- Story labels map to the user stories in `spec.md`.
- Tests should be written before implementation for the corresponding story.
- Stop at each checkpoint and validate the story independently.
- Keep generator code deterministic and metadata-only; do not execute package code.

## Runtime Builder Infrastructure Addendum

- [X] T096 [US3] Add abstract feature infrastructure requirement DTOs to `src/ValenceControl.PackageManifests/Infrastructure/InfrastructureRequirementManifest.cs`
- [X] T097 [US3] Extend override models and schemas with feature-level infrastructure requirements in `src/ValenceControl.PackageManifest.Generator.Core/Overrides/ManifestOverrideModels.cs`
- [X] T098 [US3] Emit override-declared infrastructure requirements from generated manifests in `src/ValenceControl.PackageManifest.Generator.Core/Generation/ManifestGenerator.cs`
- [X] T099 [P] [US3] Add manifest contract and generator override tests in `tests/ValenceControl.PackageManifests.Tests/` and `tests/ValenceControl.PackageManifest.Generator.Core.Tests/FeatureDiscoveryTests.cs`
