# Tasks: Server-Side Bundle Generation

**Input**: Design documents from `/specs/009-server-bundle-generation/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/builder-bundle-api.md`, `quickstart.md`

**Tests**: Test tasks are included because the spec, plan, contract, and quickstart define independent test scenarios for each story.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files or has no dependency on incomplete tasks
- **[Story]**: Which user story the task belongs to (`US1`, `US2`, `US3`, `US4`)
- Every task includes exact repository file paths

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the existing API/Core test surface for bundle generation.

- [X] T001 Inspect existing builder endpoint, workspace builder endpoint, compatibility, and public catalog query patterns in `src/Elsa.Catalog.Api/Public/Builder/BuilderEndpoints.cs`, `src/Elsa.Catalog.Api/Workspace/WorkspaceBuilderEndpoints.cs`, and `src/Elsa.Catalog.Core/Compatibility/CompatibilityCheckService.cs`
- [X] T002 [P] Create empty bundle API test file `tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs`
- [X] T003 [P] Create empty workspace bundle API test file `tests/Elsa.Catalog.Api.Tests/WorkspaceBuilderBundleApiTests.cs`
- [X] T004 [P] Create empty core bundle generation test file `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T005 [P] Create bundle renderer test fixture helpers in `tests/Elsa.Catalog.Testing/BuilderBundleFixtureBuilder.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, validation primitives, runtime image metadata, and authorization setup required before any story can ship.

**Critical**: No user story implementation should begin until these shared contracts and services exist.

- [X] T006 Define bundle request/result/domain records in `src/Elsa.Catalog.Core/Builder/BundleGenerationModels.cs`
- [X] T007 Define runtime image metadata records and seeded initial image catalog abstraction in `src/Elsa.Catalog.Core/Builder/RuntimeImageCatalog.cs`
- [X] T008 Define bundle finding helpers and blocking/error policy in `src/Elsa.Catalog.Core/Builder/BundleFindingPolicy.cs`
- [X] T009 Define bundle file safety validation for relative paths and required/optional file metadata in `src/Elsa.Catalog.Core/Builder/BundleFilePolicy.cs`
- [X] T010 Create renderer interface and ordered renderer registration model in `src/Elsa.Catalog.Core/Builder/Renderers/BundleFileRenderer.cs`
- [X] T011 Add builder bundle API request/response records to `src/Elsa.Catalog.Api/Public/Builder/BuilderContracts.cs`
- [X] T012 Add a dedicated builder-client API-key authentication and authorization policy that does not grant admin access in `src/Elsa.Catalog.Api/Authentication/BuilderClientAuthorization.cs`
- [X] T013 Register runtime image catalog, bundle generation service, renderers, and related policies in `src/Elsa.Catalog.Api/Program.cs`

**Checkpoint**: Foundation ready; story work can proceed.

---

## Phase 3: User Story 1 - Generate A Deployment Bundle From Builder Intent (Priority: P1) MVP

**Goal**: A trusted builder client can submit valid runtime builder intent and receive all required deployment files as ephemeral response data.

**Independent Test**: Submit a representative valid builder request and verify required files, file metadata, selected package/source/feature reflection, deterministic order, and optional `Program.Generated.cs` behavior.

### Tests for User Story 1

- [X] T014 [P] [US1] Add core test for minimal valid bundle returning required files in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T015 [US1] Add core test for deterministic file ordering and byte-equivalent output for the same intent in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T016 [P] [US1] Add API integration test for trusted `POST /api/builder/bundle` success in `tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs`
- [X] T017 [US1] Add API integration test rejecting direct untrusted `POST /api/builder/bundle` calls in `tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs`
- [X] T018 [US1] Add API integration test proving admin credentials are not required and dedicated builder-client credentials do not authorize admin APIs in `tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs`

### Implementation for User Story 1

- [X] T019 [US1] Implement `BundleGenerationService` orchestration for valid public builder intent in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T020 [P] [US1] Implement `config.json` renderer in `src/Elsa.Catalog.Core/Builder/Renderers/AppSettingsBundleRenderer.cs`
- [X] T021 [P] [US1] Implement `packages.lock.json` renderer in `src/Elsa.Catalog.Core/Builder/Renderers/PackageLockBundleRenderer.cs`
- [X] T022 [P] [US1] Implement `docker-compose.yml` renderer in `src/Elsa.Catalog.Core/Builder/Renderers/DockerComposeBundleRenderer.cs`
- [X] T023 [P] [US1] Implement `.env.example` renderer in `src/Elsa.Catalog.Core/Builder/Renderers/EnvExampleBundleRenderer.cs`
- [X] T024 [P] [US1] Implement `README.md` renderer in `src/Elsa.Catalog.Core/Builder/Renderers/ReadmeBundleRenderer.cs`
- [X] T025 [P] [US1] Implement optional `Program.Generated.cs` reference renderer in `src/Elsa.Catalog.Core/Builder/Renderers/ProgramReferenceBundleRenderer.cs`
- [X] T026 [US1] Implement public package/source normalization using existing public catalog visibility in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T027 [US1] Map core bundle results to API response DTOs in `src/Elsa.Catalog.Api/Public/Builder/BuilderEndpoints.cs`
- [X] T028 [US1] Add protected `POST /api/builder/bundle` route in `src/Elsa.Catalog.Api/Public/Builder/BuilderEndpoints.cs`

**Checkpoint**: User Story 1 is independently functional for successful trusted public bundle generation.

---

## Phase 4: User Story 2 - Preserve Bundle Findings And Non-Blocking Warnings (Priority: P1)

**Goal**: Generation findings are structured, safe, and correctly determine whether files are returned.

**Independent Test**: Submit warning-only and blocking-error requests and verify warnings return files, blocking errors return no files, and secrets are never exposed.

### Tests for User Story 2

- [X] T029 [P] [US2] Add core test for unknown runtime image returning error findings and no files in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T030 [US2] Add core test for missing package or invisible package returning error findings and no files in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T031 [US2] Add core test for placeholder warnings returning files with warning findings in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T032 [US2] Add core test proving secret values do not appear in files, findings, or diagnostics in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`
- [X] T033 [P] [US2] Add API integration test for blocked response shape with empty `files` in `tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs`
- [X] T034 [US2] Add core test proving generation uses local catalog data only and does not call external package registries in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`

### Implementation for User Story 2

- [X] T035 [US2] Add validation for runtime image, package selection, package visibility, selected features, infrastructure providers, local package paths, and required settings in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T036 [US2] Integrate existing `CompatibilityCheckService` findings into bundle finding output in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T037 [US2] Enforce blocking-error behavior that skips rendering and returns no files in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T038 [US2] Add secret redaction and placeholder materialization rules in `src/Elsa.Catalog.Core/Builder/BundleFindingPolicy.cs`
- [X] T039 [US2] Add non-secret generation diagnostic logging around bundle generation in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T040 [US2] Map finding `level`, `code`, `message`, and `scope` fields consistently in `src/Elsa.Catalog.Api/Public/Builder/BuilderContracts.cs`

**Checkpoint**: User Story 2 is independently functional for safe warning/error behavior.

---

## Phase 5: User Story 3 - Review Existing Browser Output During Migration (Priority: P2)

**Goal**: Migration fixtures compare current browser-shaped builder states against the new backend bundle contract without requiring exact browser parity.

**Independent Test**: Run representative migration fixtures and verify backend output satisfies the platform bundle contract while notable browser-output differences are visible for rollout review.

### Tests for User Story 3

- [X] T041 [P] [US3] Add migration fixture JSON for minimal combined runtime in `tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/minimal-combined.json`
- [X] T042 [P] [US3] Add migration fixture JSON for PostgreSQL and RabbitMQ sidecars in `tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/postgres-rabbitmq.json`
- [X] T043 [P] [US3] Add migration fixture JSON for local packages and custom source selections in `tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/local-packages-custom-source.json`
- [X] T044 [P] [US3] Add migration fixture JSON for secret setting placeholders in `tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/secret-placeholders.json`
- [X] T045 [US3] Add migration fixture contract tests that validate required backend files and surface notable differences in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`

### Implementation for User Story 3

- [X] T046 [US3] Implement fixture loader for builder bundle migration states in `tests/Elsa.Catalog.Testing/BuilderBundleFixtureBuilder.cs`
- [X] T047 [US3] Add normalized output summary helper for migration comparison in `tests/Elsa.Catalog.Testing/BuilderBundleFixtureBuilder.cs`
- [X] T048 [US3] Document accepted browser-vs-backend migration difference categories in `specs/009-server-bundle-generation/quickstart.md`

**Checkpoint**: User Story 3 is independently functional for migration fixture review.

---

## Phase 6: User Story 4 - Support Multiple Clients With The Same Bundle Contract (Priority: P3)

**Goal**: Public/trusted, workspace, and future clients share the same core bundle contract and generation service.

**Independent Test**: Submit equivalent public/trusted and workspace-visible requests and verify equivalent file contract behavior, while workspace authorization and private source visibility remain enforced.

### Tests for User Story 4

- [X] T049 [P] [US4] Add workspace member success test for `POST /api/workspaces/{workspaceId}/builder/bundle` in `tests/Elsa.Catalog.Api.Tests/WorkspaceBuilderBundleApiTests.cs`
- [X] T050 [US4] Add workspace anonymous and non-member rejection tests for `POST /api/workspaces/{workspaceId}/builder/bundle` in `tests/Elsa.Catalog.Api.Tests/WorkspaceBuilderBundleApiTests.cs`
- [X] T051 [US4] Add workspace private source non-leakage test for known foreign source IDs in `tests/Elsa.Catalog.Api.Tests/WorkspaceBuilderBundleApiTests.cs`
- [X] T052 [P] [US4] Add core test proving equivalent normalized intent returns equivalent files across public and workspace visibility contexts in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`

### Implementation for User Story 4

- [X] T053 [US4] Extend `BundleGenerationService` to accept optional workspace visibility context in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T054 [US4] Add workspace-visible package/source normalization using existing workspace catalog queries in `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T055 [US4] Add `POST /api/workspaces/{workspaceId}/builder/bundle` route using existing workspace access checks in `src/Elsa.Catalog.Api/Workspace/WorkspaceBuilderEndpoints.cs`
- [X] T056 [US4] Reuse public builder bundle DTO response mapping for workspace route in `src/Elsa.Catalog.Api/Workspace/WorkspaceBuilderEndpoints.cs`

**Checkpoint**: User Story 4 is independently functional for shared multi-client bundle contract behavior.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validate safety, docs, and repo quality after desired stories are complete.

- [X] T057 [P] Update API examples for bundle generation in `src/Elsa.Catalog.Api/Elsa.Catalog.Api.http`
- [X] T058 [P] Update quickstart validation notes after implementation in `specs/009-server-bundle-generation/quickstart.md`
- [X] T059 Review bundle renderer abstractions for unnecessary indirection and simplify files under `src/Elsa.Catalog.Core/Builder/Renderers/`
- [X] T060 Run `dotnet build Elsa.PackageCatalog.sln --no-restore` against `Elsa.PackageCatalog.sln`
- [X] T061 Run `dotnet test Elsa.PackageCatalog.sln --no-build` against `Elsa.PackageCatalog.sln`
- [X] T062 Confirm no generated file contents, secret values, or unsanitized private source URLs are persisted by reviewing `src/Elsa.Catalog.Core/Builder/BundleGenerationService.cs`
- [X] T063 Add performance test or timed integration assertion for representative bundle generation under 1 second in `tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup** has no dependencies.
- **Phase 2: Foundational** depends on Phase 1 and blocks all user stories.
- **Phase 3: US1** depends on Phase 2 and is the first successful-generation slice.
- **Phase 4: US2** depends on Phase 2 and can run alongside US1 after shared models exist, but final endpoint behavior should be validated after US1 rendering exists.
- **Phase 5: US3** depends on US1 and US2 because fixtures validate the new backend contract and safety behavior.
- **Phase 6: US4** depends on Phase 2 and can run alongside US1/US2 if it uses the same core service carefully.
- **Phase 7: Polish** depends on the selected story phases being complete.

### User Story Dependencies

- **US1 (P1)**: Requires Foundation only; delivers MVP successful bundle generation.
- **US2 (P1)**: Requires Foundation and integrates with US1 rendering; required before external rollout.
- **US3 (P2)**: Requires US1 and US2 to define the backend contract and finding behavior.
- **US4 (P3)**: Requires Foundation and shared service shape; independent of migration fixtures.

### Parallel Opportunities

- T002, T003, T004, and T005 can run in parallel.
- Renderer tasks T020 through T025 can run in parallel after T010 and T019 define shared inputs.
- Migration fixture tasks T041 through T044 can run in parallel.
- US2 test tasks T029 and T033 can run in parallel because they edit different test files.
- US4 test tasks T049 and T052 can run in parallel because they edit different test files.

---

## Parallel Example: User Story 1

```text
Task: "T014 [P] [US1] Add core test for minimal valid bundle returning required files in tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs"
Task: "T016 [P] [US1] Add API integration test for trusted POST /api/builder/bundle success in tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs"
Task: "T020 [P] [US1] Implement config.json renderer in src/Elsa.Catalog.Core/Builder/Renderers/AppSettingsBundleRenderer.cs"
Task: "T021 [P] [US1] Implement packages.lock.json renderer in src/Elsa.Catalog.Core/Builder/Renderers/PackageLockBundleRenderer.cs"
Task: "T022 [P] [US1] Implement docker-compose.yml renderer in src/Elsa.Catalog.Core/Builder/Renderers/DockerComposeBundleRenderer.cs"
```

## Parallel Example: User Story 2

```text
Task: "T029 [P] [US2] Add core test for unknown runtime image returning error findings and no files in tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs"
Task: "T033 [P] [US2] Add API integration test for blocked response shape with empty files in tests/Elsa.Catalog.Api.Tests/PublicBuilderBundleApiTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T041 [P] [US3] Add migration fixture JSON for minimal combined runtime in tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/minimal-combined.json"
Task: "T042 [P] [US3] Add migration fixture JSON for PostgreSQL and RabbitMQ sidecars in tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/postgres-rabbitmq.json"
Task: "T043 [P] [US3] Add migration fixture JSON for local packages and custom source selections in tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/local-packages-custom-source.json"
Task: "T044 [P] [US3] Add migration fixture JSON for secret setting placeholders in tests/Elsa.Catalog.Testing/Fixtures/BuilderBundles/secret-placeholders.json"
```

## Parallel Example: User Story 4

```text
Task: "T049 [P] [US4] Add workspace member success test for POST /api/workspaces/{workspaceId}/builder/bundle in tests/Elsa.Catalog.Api.Tests/WorkspaceBuilderBundleApiTests.cs"
Task: "T052 [P] [US4] Add core test proving equivalent normalized intent returns equivalent files across public and workspace visibility contexts in tests/Elsa.Catalog.Core.Tests/BuilderBundleGenerationTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1) for successful trusted public bundle generation.
3. Validate US1 independently with core and API tests.
4. Complete Phase 4 (US2) before exposing the endpoint to Lovable, because warning/error safety is part of the P1 contract.

### Incremental Delivery

1. Foundation establishes shared models, image metadata, file policy, and auth registration.
2. US1 adds successful generation and the protected public bundle endpoint.
3. US2 adds blocking findings, warning behavior, secret safety, and diagnostics.
4. US4 adds workspace/private-source support using the same service contract.
5. US3 adds migration fixture coverage for rollout review.
6. Polish validates quickstart commands and removes unnecessary abstractions.

### Parallel Team Strategy

1. One developer owns foundational models and service orchestration.
2. Renderer tasks can be split by output file once the renderer interface exists.
3. API route work can proceed in parallel with renderer implementation after contracts are stable.
4. Workspace route work should avoid editing the same public endpoint files as US1 to reduce conflicts.

## Notes

- Tasks marked `[P]` avoid same-file conflicts or can run before dependent implementation tasks.
- Every story has explicit independent tests and a checkpoint.
- Generated files remain ephemeral; do not add database tables or retrieval endpoints in this feature.
- Bundle endpoints use dedicated builder-client credentials, not broad admin credentials.
- `Program.Generated.cs` is optional reference output only.
- Backend output is validated against the new platform bundle contract, not exact browser parity.
