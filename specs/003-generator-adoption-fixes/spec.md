# Feature Specification: Generator Adoption Fixes for Elsa Shell Modules

**Feature Branch**: `codex/003-generator-adoption-fixes`

**Created**: 2026-05-15

**Status**: Draft

**Input**: User description: "Improve `ValenceControl.PackageManifest.Generator` adoption by Elsa Core shell-feature modules by fixing warning severity task failures, excluding delegate/service-factory configuration hooks from deploy-time settings, and ensuring multi-targeted packages include exactly one root `elsa-package.json` without consumer-side targets."

## Clarifications

### Session 2026-05-15

- Q: Should ignored delegate-shaped hooks produce default warnings that can trigger fail-on-warnings? → A: No; log low-importance or verbose diagnostics only, with no default warning.
- Q: What should validation severity set to warning downgrade? → A: Manifest validation findings only; infrastructure and invalid input failures still fail.
- Q: Which target framework supplies the canonical package manifest when manifest surfaces are equivalent? → A: The first declared target framework.
- Q: Should unsupported non-delegate property types such as `System.Type` or complex option objects fail normal builds? → A: No; omit the property from manifest settings, log a low-importance non-warning diagnostic, and allow the build to complete.

### Session 2026-05-16

- Q: Should `dotnet pack --no-build` regenerate the manifest after a successful build? → A: No; it must reuse the existing intermediate manifest and fail clearly only when package inclusion is required and the manifest is missing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Adopt the Generator Without Custom Project Workarounds (Priority: P1)

An Elsa Core module maintainer adds a private reference to the preview generator package in a class library module and can build and pack the module without adding custom targets or manifest item workarounds.

**Why this priority**: The generator only delivers value if many Elsa modules can adopt it with a small, repeatable project change.

**Independent Test**: Create a representative multi-targeted Elsa module package with a shell feature, add only the private generator package reference, build it, pack it, and inspect the package contents.

**Acceptance Scenarios**:

1. **Given** a multi-targeted module project references `ValenceControl.PackageManifest.Generator` with private assets, **When** the maintainer runs a normal build, **Then** the build succeeds and creates a manifest for the discovered shell features.
2. **Given** the same project has no custom manifest packaging targets, **When** the maintainer packs the project, **Then** the package contains exactly one `elsa-package.json` at the package root.
3. **Given** a project targets one or many frameworks, **When** the package is created, **Then** duplicate root manifests are not produced and equivalent multi-target surfaces use the first declared target framework as the canonical package manifest source.

---

### User Story 2 - Treat Non-Manifestable Properties as Non-Configurable (Priority: P1)

An Elsa Core module maintainer exposes fluent setup hooks, service factories, HTTP client configuration callbacks, delegate-valued collections, or other CLR-only property types on shell features without those properties being treated as deploy-time settings.

**Why this priority**: Existing Elsa shell features commonly expose code configuration hooks and CLR-only values such as provider `Type` references that cannot be represented as deployment configuration. Treating them as settings blocks adoption across otherwise valid modules.

**Independent Test**: Build a shell-feature module that exposes public settable delegate-shaped properties and unsupported non-delegate properties such as `System.Type`, then verify that the manifest is still generated without unsupported-setting failures and includes only supported deploy-time settings.

**Acceptance Scenarios**:

1. **Given** a shell feature exposes a public settable property shaped like an action callback, **When** manifest generation runs, **Then** the property is excluded from configurable settings by default.
2. **Given** a shell feature exposes a public settable property shaped like a factory callback, **When** manifest generation runs, **Then** the property is excluded from configurable settings by default.
3. **Given** a shell feature exposes a collection or dictionary whose values are delegate-shaped factories or callbacks, **When** manifest generation runs, **Then** the property is excluded from configurable settings by default.
4. **Given** delegate-shaped properties are excluded, **When** normal diagnostics are emitted, **Then** the build does not log warnings for those exclusions by default.
5. **Given** a normal deploy-time setting is public and settable, **When** manifest generation runs, **Then** that setting is still discovered and represented in the manifest.
6. **Given** a shell feature exposes a public settable property with an unsupported CLR-only type, **When** manifest generation runs, **Then** the property is excluded from configurable settings, a low-importance non-warning diagnostic identifies the omission, and the build completes.

---

### User Story 3 - Warning Severity Does Not Fail the Build Unless Requested (Priority: P1)

A build engineer can temporarily map generator validation issues to warnings during staged adoption and receive warnings without the build failing.

**Why this priority**: Warning-only adoption is the practical path for rolling the generator through many existing modules while still surfacing issues to maintainers.

**Independent Test**: Build a sample module with a configurable generator finding under warning severity, first with fail-on-warnings disabled and then enabled, and compare the build result and diagnostics.

**Acceptance Scenarios**:

1. **Given** validation severity is configured as warning and fail-on-warnings is false, **When** generation produces mapped warning diagnostics, **Then** the build succeeds and logs warnings.
2. **Given** validation severity is configured as warning and fail-on-warnings is true, **When** generation produces mapped warning diagnostics, **Then** the build fails with clear warning diagnostics.
3. **Given** validation severity uses the default error policy, **When** required manifest validation errors occur, **Then** the build fails.
4. **Given** diagnostics are mapped from errors to warnings, **When** the build completes successfully, **Then** no misleading "task returned false but did not log an error" build failure appears.

---

### User Story 4 - Preserve Required Manifest Quality Gates (Priority: P2)

A catalog or runtime builder maintainer can rely on default generator behavior to reject packages with required manifest errors while still allowing warning-based rollout when explicitly configured.

**Why this priority**: Adoption fixes must not weaken the default manifest quality guarantees that protect package catalog ingestion and runtime tooling.

**Independent Test**: Generate manifests with required schema failures and recommended metadata findings under default and warning-adoption policies, and verify the expected pass/fail behavior.

**Acceptance Scenarios**:

1. **Given** a generated manifest is missing required package or feature data, **When** the default policy is used, **Then** generation fails the build with actionable diagnostics.
2. **Given** a generated manifest has recommended metadata findings only, **When** the default policy is used, **Then** the build reports warnings without failing unless fail-on-warnings is enabled.
3. **Given** a team explicitly chooses warning severity for adoption, **When** required manifest validation findings are mapped to warnings, **Then** the build succeeds only when fail-on-warnings is false.

### Edge Cases

- A shell feature exposes nested generic delegate shapes such as dictionaries, lists, arrays, nullable wrappers, or interface-based collections containing callback or factory delegates.
- A shell feature exposes both delegate-shaped hooks and normal deploy-time settings in the same type.
- A delegate-shaped property has documentation or manifest hint metadata but remains a code hook unless explicitly supported by a future configurable-setting contract.
- A non-delegate complex object setting, `System.Type` setting, or other CLR-only setting shape remains unsupported, is omitted from the manifest, and emits only a low-importance diagnostic by default.
- Multiple target frameworks produce the same manifest-relevant shell-feature surface.
- Multiple target frameworks produce different manifest-relevant shell-feature surfaces.
- A package is packed directly without an earlier separate build.
- A package is built and then packed with `--no-build`, so package references may not be fully resolved during pack.
- A package is packed with `--no-build` before the manifest exists.
- A project customizes the manifest package path.
- Fail-on-warnings is enabled while all diagnostics are warnings.
- Warning severity is selected but the generator encounters an unrecoverable infrastructure or invalid input error, such as an unreadable assembly or invalid override file.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A consuming Elsa module MUST be able to adopt the generator with a single private package reference and no custom build targets for the default build and pack workflow.
- **FR-002**: A normal build of a consuming module MUST succeed when delegate-shaped code configuration hooks are present and all remaining findings are warnings under the selected policy.
- **FR-003**: A normal pack of a multi-targeted consuming module MUST include exactly one root `elsa-package.json` by default.
- **FR-004**: The generated manifest MUST continue to include discoverable shell features and supported deploy-time settings when delegate-shaped hooks are present.
- **FR-005**: Direct delegate-shaped public settable properties MUST be excluded from deploy-time setting discovery by default.
- **FR-006**: Public settable properties whose collection or dictionary element values are delegate-shaped callbacks or factories MUST be excluded from deploy-time setting discovery by default.
- **FR-007**: Excluded delegate-shaped properties MUST NOT produce unsupported-setting errors under the default workflow.
- **FR-008**: Excluded delegate-shaped properties MUST NOT produce warnings by default and MAY produce concise low-importance or verbose diagnostics that identify them as ignored code configuration hooks when verbose diagnostics are enabled.
- **FR-009**: Non-delegate unsupported setting shapes MUST be excluded from deploy-time setting discovery by default.
- **FR-009a**: Excluded unsupported setting shapes MUST emit low-importance non-warning diagnostics by default and MUST NOT fail builds, including when fail-on-warnings is enabled.
- **FR-009b**: Unsupported setting omissions MUST include enough context to identify the owning feature, property name, and CLR type.
- **FR-010**: Validation severity set to warning MUST log mapped manifest validation findings as warnings.
- **FR-011**: Validation severity set to warning MUST allow the task to succeed when fail-on-warnings is false and no infrastructure or invalid input failure occurs.
- **FR-012**: Validation severity set to warning MUST fail the task when fail-on-warnings is true and warnings are present.
- **FR-013**: The default validation policy MUST continue to fail builds for required manifest schema errors.
- **FR-014**: Recommended metadata findings MUST remain warnings by default and MUST fail only when fail-on-warnings is enabled or another explicit strict policy requires it.
- **FR-015**: The task result MUST be consistent with the diagnostics it logs so a build never fails with a task-returned-false message when only warnings were logged.
- **FR-015a**: Infrastructure and invalid input failures MUST fail the build regardless of validation severity.
- **FR-016**: Multi-targeted package projects MUST avoid duplicate root manifest package entries.
- **FR-017**: Multi-targeted package projects MUST choose the first declared target framework as the canonical manifest source for package root inclusion when target frameworks produce equivalent manifest-relevant output.
- **FR-018**: Multi-targeted package projects MUST report target-framework manifest differences according to the configured severity policy.
- **FR-019**: Direct pack operations MUST generate and include the root manifest without requiring a prior explicit build.
- **FR-019a**: Build-then-pack pipelines using `dotnet pack --no-build` MUST include the existing generated manifest without rerunning metadata inspection.
- **FR-019b**: When package inclusion is enabled and `dotnet pack --no-build` cannot find the required intermediate manifest, packing MUST fail with an actionable message to build first, pack without `--no-build`, or disable manifest inclusion.
- **FR-020**: Custom manifest package paths MUST continue to be honored while preserving the one-manifest default behavior.
- **FR-021**: Tests MUST cover task return behavior for warning severity with fail-on-warnings both false and true.
- **FR-022**: Tests MUST cover delegate-shaped direct properties and delegate-shaped collection or dictionary values.
- **FR-022a**: Tests MUST cover unsupported non-delegate properties, including `System.Type`, and verify that they are omitted without warnings or errors.
- **FR-023**: Tests MUST cover multi-target package inclusion and verify exactly one root manifest in the produced package.
- **FR-024**: Tests MUST cover that required schema validation errors still fail under the default policy.

### Key Entities *(include if feature involves data)*

- **Generator Adoption Policy**: The effective build behavior chosen by validation severity, strictness, and fail-on-warnings settings.
- **Deploy-Time Setting**: A shell-feature property that can be represented in package manifest configuration metadata.
- **Code Configuration Hook**: A shell-feature property whose value is a callback, factory, or collection of callbacks/factories intended for application code rather than deployment configuration.
- **Canonical Package Manifest**: The single `elsa-package.json` included at the package root for a packed consuming module.
- **Target Framework Manifest Surface**: The manifest-relevant shell features and settings discovered for one target framework before choosing the canonical package manifest.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A representative multi-target Elsa shell-feature module builds and packs successfully after adding only the private generator package reference.
- **SC-002**: Package inspection for a representative multi-target module finds exactly one root `elsa-package.json` and no duplicate root manifest entries.
- **SC-002a**: Equivalent multi-target packages use the first declared target framework as the canonical manifest source in every covered package inspection test.
- **SC-002b**: A representative module succeeds with `dotnet build` followed by `dotnet pack --no-build`, even when metadata reference paths available during build are no longer available during pack.
- **SC-003**: Representative shell features with action callbacks, service factories, HTTP-client callbacks, and delegate-valued dictionaries or lists generate manifests without unsupported-setting build failures.
- **SC-003a**: Ignored delegate-shaped hooks do not cause fail-on-warnings builds to fail unless another warning or error is present.
- **SC-003b**: Representative shell features with `System.Type` or complex object properties generate manifests that omit those properties and do not fail fail-on-warnings builds unless another warning or error is present.
- **SC-004**: Warning severity with fail-on-warnings disabled produces a successful build with warning diagnostics in every covered test case.
- **SC-005**: Warning severity with fail-on-warnings enabled produces a failed build in every covered warning-diagnostic test case.
- **SC-006**: Default validation policy still fails every covered required-schema-error test case.
- **SC-007**: No covered warning-only scenario emits the misleading task-returned-false build failure.
- **SC-008**: Warning severity does not allow covered infrastructure or invalid input failures to produce a successful build.

## Assumptions

- Elsa Core shell-feature modules expose CShells `IShellFeature` types through class library packages.
- Delegate-shaped hooks are application-code extension points and are not useful as deploy-time configuration settings in the current manifest contract.
- Unsupported CLR-only property shapes are not useful as deploy-time configuration settings until the manifest contract explicitly supports them.
- Excluding delegate-shaped hooks by default is preferred over requiring every existing module to annotate those hooks individually.
- Existing explicit ignore metadata remains supported and continues to take precedence.
- Target-framework-specific manifest differences remain out of the default happy path and should continue to be diagnosed.
- The feature is a focused adoption hardening pass for the existing generator, not a redesign of the manifest contract.
