# Tasks: Elsa Control Self-Healing

**Input**: Design documents from `/specs/039-elsa-control-self-healing/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Required. Each user story starts with failing contract/unit/integration tests and ends with its independent acceptance checkpoint.

**Organization**: Tasks are grouped by user story. Paths prefixed with `elsa-foundation/` are relative to that coordinated repository; all other paths are relative to `elsa-control`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it targets different files and has no dependency on an incomplete task in the same phase.
- **[Story]**: Maps to the numbered user story in `spec.md`.

## Phase 1: Setup and project boundaries

**Purpose**: Establish clean cross-repository branches and explicit package boundaries before behavior work.

- [x] T001 Add Healing Abstractions, Core, Agent, GitHub, OpenTelemetry, Client, ComponentManifest, ComponentManifest.Generator.MSBuild, EF persistence, and provider migration projects to `ElsaControl.sln`
- [x] T002 [P] Add matching Healing test projects and references to `ElsaControl.sln`
- [x] T003 [P] Add centrally managed Healing/Foundation/GitHub/JWT dependency versions to `Directory.Packages.props`
- [x] T004 [P] Create dependency-light project references in `src/ElsaControl.Healing.Abstractions/ElsaControl.Healing.Abstractions.csproj`
- [x] T005 [P] Create domain project references in `src/ElsaControl.Healing.Core/ElsaControl.Healing.Core.csproj`
- [x] T006 [P] Create adapter and persistence project references in `src/ElsaControl.Healing.GitHub/ElsaControl.Healing.GitHub.csproj`, `src/ElsaControl.Healing.Agent/ElsaControl.Healing.Agent.csproj`, `src/ElsaControl.Healing.OpenTelemetry/ElsaControl.Healing.OpenTelemetry.csproj`, and `src/ElsaControl.Healing.Persistence.EntityFrameworkCore/ElsaControl.Healing.Persistence.EntityFrameworkCore.csproj`
- [x] T007 [P] Create publishable client/manifest/MSBuild project metadata in `src/ElsaControl.Healing.Client/ElsaControl.Healing.Client.csproj`, `src/ElsaControl.Healing.ComponentManifest/ElsaControl.Healing.ComponentManifest.csproj`, and `src/ElsaControl.Healing.ComponentManifest.Generator.MSBuild/ElsaControl.Healing.ComponentManifest.Generator.MSBuild.csproj`
- [x] T008 [P] Add Healing projects to API and test composition in `src/ElsaControl.Api/ElsaControl.Api.csproj` and `tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj`
- [x] T009 Remove unconditional identity PII logging and add an explicit local-development switch in `src/ElsaControl.Api/Program.cs`
- [x] T010 Verify required .NET, Node, Docker, and universal ignore patterns in `.gitignore`, `.dockerignore`, and `src/ElsaControl.Console/.gitignore`

---

## Phase 2: Foundational contracts, durability, security, and audit

**Purpose**: Implement the cross-repository ingestion seam and the authoritative infrastructure required by every user story.

**⚠️ CRITICAL**: No user-story implementation starts until this phase passes.

### Foundation contribution seam (tests first)

- [x] T011 [P] Add failing ordering/redaction/failure/cancellation tests to `elsa-foundation/tests/Elsa/Diagnostics/OpenTelemetry/Tests/OpenTelemetryIngestorTests.cs`
- [x] T012 [P] Add failing additive DI resolution tests to `elsa-foundation/tests/Elsa/Diagnostics/OpenTelemetry/Tests/OpenTelemetryFeatureTests.cs`
- [x] T013 Add `IOpenTelemetryIngestionContributor` to `elsa-foundation/src/Elsa/Diagnostics/OpenTelemetry/Core/Contracts/IOpenTelemetryIngestionContributor.cs`
- [x] T014 Invoke all contributors after redaction and before store/live publication in `elsa-foundation/src/Elsa/Diagnostics/OpenTelemetry/Services/OpenTelemetryIngestor.cs`
- [x] T015 Add additive contributor registration helpers in `elsa-foundation/src/Elsa/Diagnostics/OpenTelemetry/Extensions/ServiceCollectionExtensions.cs`
- [x] T016 [P] Document the contribution contract and durability semantics in `elsa-foundation/src/Elsa/Diagnostics/OpenTelemetry/README.md` and `elsa-foundation/src/Elsa/Diagnostics/OpenTelemetry/EXTENSION_POINTS.md`
- [x] T017 Run Foundation OpenTelemetry tests and pack coordinated local packages from `elsa-foundation/src/Elsa/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry.csproj`

### Elsa Control shared contracts and persistence (tests first)

- [x] T018 [P] Add failing package-boundary and contract-version tests in `tests/ElsaControl.Healing.Abstractions.Tests/HealingBoundaryTests.cs`
- [x] T019 [P] Add failing SQLite/SQL Server model, uniqueness, lease, and append-only audit tests in `tests/ElsaControl.Healing.Persistence.EntityFrameworkCore.Tests/HealingDbContextTests.cs`
- [x] T020 Define versioned signal, manifest, provider, agent, workload, policy, deployment, and audit contracts in `src/ElsaControl.Healing.Abstractions/HealingContracts.cs`
- [x] T021 [P] Define stable Healing permissions and actor/command vocabularies in `src/ElsaControl.Healing.Abstractions/HealingPermissions.cs`
- [x] T022 [P] Define shared entity/state enums and transition result types in `src/ElsaControl.Healing.Core/HealingModels.cs`
- [x] T023 Implement `HealingDbContext` and entity mappings in `src/ElsaControl.Healing.Persistence.EntityFrameworkCore/HealingDbContext.cs` and `src/ElsaControl.Healing.Persistence.EntityFrameworkCore/HealingModelConfiguration.cs`
- [x] T024 Implement provider selection and separate migration-history configuration in `src/ElsaControl.Healing.Persistence.EntityFrameworkCore/HealingDatabaseServiceCollectionExtensions.cs`
- [x] T025 [P] Add initial SQLite migration and snapshot in `src/ElsaControl.Healing.Persistence.SqliteMigrations/Migrations/20260716000000_InitialHealing.cs` and `src/ElsaControl.Healing.Persistence.SqliteMigrations/Migrations/HealingDbContextModelSnapshot.cs`
- [x] T026 [P] Add initial SQL Server migration and snapshot in `src/ElsaControl.Healing.Persistence.SqlServerMigrations/Migrations/20260716000000_InitialHealing.cs` and `src/ElsaControl.Healing.Persistence.SqlServerMigrations/Migrations/HealingDbContextModelSnapshot.cs`
- [x] T027 Implement idempotent inbox, leased operation, configuration, manifest, incident, attempt, verification, and append-only audit stores in `src/ElsaControl.Healing.Persistence.EntityFrameworkCore/HealingStore.cs`
- [x] T028 Implement Healing options, application/platform kill switches, bounded budgets, and validation in `src/ElsaControl.Healing.Core/Configuration/HealingOptions.cs`
- [x] T029 Implement append/query-only audit service with safe structured details in `src/ElsaControl.Healing.Core/Security/HealingAuditService.cs`
- [x] T030 Implement reusable leased background-operation worker and stale recovery in `src/ElsaControl.Healing.Core/Operations/HealingOperationWorker.cs`
- [x] T031 Add `AddControlHealing` composition and hosted-worker registration in `src/ElsaControl.Api/Healing/HealingServiceCollectionExtensions.cs`
- [x] T032 Register Healing persistence migrations, services, and endpoint modules in `src/ElsaControl.Api/Program.cs`

**Checkpoint**: Foundation contributor tests, Healing boundary tests, both persistence-provider model tests, audit immutability, and leased operation recovery pass.

---

## Phase 3: User Story 1 — Configure Repairable Application Components (Priority: P1) 🎯 MVP

**Goal**: Enable Healing per application/environment, register trusted revision manifests, and approve unambiguous component-to-repository ownership.

**Independent Test**: Register first/third-party components and one approved binding; only selected components become repairable, while suggestions and conflicting bindings remain non-authoritative.

### Tests for User Story 1

- [x] T033 [P] [US1] Add failing canonicalization, hashing, path-safety, dependency-graph, and secret-exclusion tests in `tests/ElsaControl.Healing.ComponentManifest.Tests/ComponentManifestTests.cs`
- [x] T034 [P] [US1] Add failing resolved-assets/MSBuild generation tests in `tests/ElsaControl.Healing.ComponentManifest.Generator.MSBuild.Tests/GenerateHealingComponentManifestTaskTests.cs`
- [x] T035 [P] [US1] Add failing configuration, trust, selector overlap, suggestion, and authorization tests in `tests/ElsaControl.Healing.Core.Tests/Ownership/SourceOwnershipServiceTests.cs`
- [x] T036 [P] [US1] Add failing workspace configuration/manifest/binding API tests in `tests/ElsaControl.Api.Tests/Healing/WorkspaceHealingConfigurationApiTests.cs`
- [x] T037 [P] [US1] Add failing components/configuration page tests in `src/ElsaControl.Console/src/features/healing/HealingConfigurationPage.test.tsx`

### Implementation for User Story 1

- [x] T038 [P] [US1] Implement canonical manifest records, serializer, validator, and digest calculator in `src/ElsaControl.Healing.ComponentManifest/ComponentManifest.cs`
- [x] T039 [P] [US1] Implement safe resolved NuGet/assembly inventory and hash generation in `src/ElsaControl.Healing.ComponentManifest.Generator.MSBuild/GenerateHealingComponentManifestTask.cs`
- [x] T040 [US1] Add build target/package assets that emit the manifest in `src/ElsaControl.Healing.ComponentManifest.Generator.MSBuild/build/ElsaControl.Healing.ComponentManifest.Generator.MSBuild.targets`
- [x] T041 [P] [US1] Implement configuration and environment override service in `src/ElsaControl.Healing.Core/Configuration/HealingConfigurationService.cs`
- [x] T042 [P] [US1] Implement manifest registration/trust/revocation service in `src/ElsaControl.Healing.Core/Manifests/ComponentManifestService.cs`
- [x] T043 [US1] Implement deterministic selector matching, ambiguity detection, metadata suggestions, and ownership activation in `src/ElsaControl.Healing.Core/Ownership/SourceOwnershipService.cs`
- [x] T044 [US1] Map configuration, manifest, binding, and emergency-stop endpoints in `src/ElsaControl.Api/Workspace/Healing/WorkspaceHealingConfigurationEndpoints.cs`
- [x] T045 [P] [US1] Add Healing API types/client queries in `src/ElsaControl.Console/src/features/healing/healingModels.ts` and `src/ElsaControl.Console/src/features/healing/healingApi.ts`
- [x] T046 [US1] Implement application configuration and component ownership UI in `src/ElsaControl.Console/src/features/healing/HealingConfigurationPage.tsx`
- [x] T047 [US1] Register Healing routes/navigation in `src/ElsaControl.Console/src/app/routes.tsx` and `src/ElsaControl.Console/src/app/AppShell.tsx`

**Checkpoint**: User Story 1 passes independently with trusted and ambiguous manifest/binding fixtures.

---

## Phase 4: User Story 2 — Turn Runtime Exceptions Into Deduplicated Incidents (Priority: P1)

**Goal**: Accept post-redaction OTLP/explicit exceptions durably, classify and fingerprint them, apply thresholds, and maintain one canonical incident/work item per fingerprint/repository.

**Independent Test**: 10,000 duplicate occurrences across instances/revisions/environments produce one active incident and preserve impact; excluded failures do not dispatch repair work.

### Tests for User Story 2

- [x] T048 [P] [US2] Add failing post-redaction durable contributor/idempotency tests in `tests/ElsaControl.Healing.OpenTelemetry.Tests/HealingOpenTelemetryContributorTests.cs`
- [x] T049 [P] [US2] Add failing signal profile normalization, curated classification, and fingerprint stability tests in `tests/ElsaControl.Healing.Core.Tests/Incidents/HealingSignalClassifierTests.cs`
- [x] T050 [P] [US2] Add failing concurrent deduplication, threshold, environment aggregation, and regression-episode tests in `tests/ElsaControl.Healing.Persistence.EntityFrameworkCore.Tests/IncidentProjectionConcurrencyTests.cs`
- [x] T051 [P] [US2] Add failing OTLP and explicit incident API tests in `tests/ElsaControl.Api.Tests/Healing/HealingIntakeApiTests.cs`
- [x] T052 [P] [US2] Add failing incident list/detail UI tests in `src/ElsaControl.Console/src/features/healing/HealingIncidentsPage.test.tsx`

### Implementation for User Story 2

- [x] T053 [P] [US2] Implement v1 profile normalization and stable occurrence identity in `src/ElsaControl.Healing.Core/Incidents/HealingSignalNormalizer.cs`
- [x] T054 [P] [US2] Implement curated classification and policy override service in `src/ElsaControl.Healing.Core/Incidents/HealingSignalClassifier.cs`
- [x] T055 [P] [US2] Implement versioned deterministic fingerprinting and normalized frame extraction in `src/ElsaControl.Healing.Core/Incidents/HealingFingerprintService.cs`
- [x] T056 [US2] Implement component attribution against the producing manifest and approved bindings in `src/ElsaControl.Healing.Core/Incidents/ComponentAttributionService.cs`
- [x] T057 [US2] Implement idempotent incident/episode/environment projection and thresholds in `src/ElsaControl.Healing.Core/Incidents/HealingIncidentService.cs`
- [x] T058 [US2] Implement the Foundation contributor bridge that only appends durable inbox items in `src/ElsaControl.Healing.OpenTelemetry/HealingOpenTelemetryIngestionContributor.cs`
- [x] T059 [US2] Implement leased inbox processing and dead-letter outcomes in `src/ElsaControl.Healing.Core/Incidents/HealingSignalInboxWorker.cs`
- [x] T060 [US2] Map Elsa Control OTLP composition/query routes and explicit incident intake in `src/ElsaControl.Api/Workspace/Healing/HealingIntakeEndpoints.cs`
- [x] T061 [US2] Implement incident list/detail queries in `src/ElsaControl.Api/Workspace/Healing/WorkspaceHealingIncidentEndpoints.cs`
- [x] T062 [US2] Implement incident list and detail pages in `src/ElsaControl.Console/src/features/healing/HealingIncidentsPage.tsx` and `src/ElsaControl.Console/src/features/healing/HealingIncidentPage.tsx`

**Checkpoint**: User Story 2 passes normal, retry, duplicate, concurrent-worker, exclusion, ambiguity, and later-regression scenarios without GitHub/inference adapters.

---

## Phase 5: User Story 3 — Produce a Governed Repair Pull Request (Priority: P1)

**Goal**: Project one GitHub issue, authenticate an approved repository workflow, deliver bounded evidence, accept an inert repair result, and publish one traceable PR through a trusted GitHub App publisher.

**Independent Test**: A seeded reproduced defect yields one linked PR with evidence/validation/risk metadata; inferred and revision-unverified results are explicit drafts; forbidden self-modification is rejected.

### Tests for User Story 3

- [x] T063 [P] [US3] Add failing evidence minimization/elevation and attempt-cap tests in `tests/ElsaControl.Healing.Core.Tests/Repairs/RepairOrchestrationTests.cs`
- [x] T064 [P] [US3] Add failing GitHub App token narrowing, issue projection, workflow dispatch, and idempotency tests in `tests/ElsaControl.Healing.GitHub.Tests/GitHubRepairProviderTests.cs`
- [x] T065 [P] [US3] Add failing GitHub OIDC issuer/audience/claim/nonce/replay tests in `tests/ElsaControl.Healing.GitHub.Tests/GitHubWorkloadIdentityValidatorTests.cs`
- [x] T066 [P] [US3] Add failing patch traversal/binary/symlink/submodule/stale-SHA/forbidden-path tests in `tests/ElsaControl.Healing.GitHub.Tests/TrustedPatchPublisherTests.cs`
- [x] T067 [P] [US3] Add failing bounded repair-gateway request/result and credential-isolation tests in `tests/ElsaControl.Healing.Agent.Tests/RepairAgentGatewayTests.cs`
- [x] T068 [P] [US3] Add failing workload evidence/heartbeat/result and webhook API tests in `tests/ElsaControl.Api.Tests/Healing/HealingRepairWorkflowApiTests.cs`
- [x] T069 [P] [US3] Add failing repair-attempt/PR UI tests in `src/ElsaControl.Console/src/features/healing/HealingRepairPanel.test.tsx`

### Implementation for User Story 3

- [x] T070 [P] [US3] Implement evidence bundle minimization, immutable tiers, and audited elevation in `src/ElsaControl.Healing.Core/Repairs/HealingEvidenceService.cs`
- [x] T071 [P] [US3] Implement bounded provider-neutral repair request/result gateway in `src/ElsaControl.Healing.Agent/RepairAgentGateway.cs`
- [x] T072 [US3] Implement attempt creation, two-attempt cap, leases, reproduction tiers, and already-fixed detection in `src/ElsaControl.Healing.Core/Repairs/RepairOrchestrationService.cs`
- [x] T073 [P] [US3] Implement GitHub App JWT and narrowed installation-token provider in `src/ElsaControl.Healing.GitHub/GitHubAppTokenProvider.cs`
- [x] T074 [P] [US3] Implement machine-owned issue projection and approved workflow dispatch in `src/ElsaControl.Healing.GitHub/GitHubRepairWorkProvider.cs`
- [x] T075 [P] [US3] Implement GitHub Actions OIDC validation and incident capability exchange in `src/ElsaControl.Healing.GitHub/GitHubWorkloadIdentityValidator.cs`
- [x] T076 [P] [US3] Implement HMAC webhook verification, delivery replay protection, and allowlisting in `src/ElsaControl.Healing.GitHub/GitHubWebhookVerifier.cs`
- [x] T077 [US3] Implement unified-diff parsing, deterministic publication policy, and trusted branch/PR publication in `src/ElsaControl.Healing.GitHub/TrustedGitHubPatchPublisher.cs`
- [x] T078 [US3] Implement leased provider-operation dispatch and retry outcomes in `src/ElsaControl.Healing.Core/Providers/ProviderOperationService.cs`
- [x] T079 [US3] Map OIDC exchange, evidence, heartbeat, result, and verified webhook routes in `src/ElsaControl.Api/Workspace/Healing/HealingRepairWorkflowEndpoints.cs`
- [x] T080 [US3] Implement repair attempt, evidence tier, and PR presentation in `src/ElsaControl.Console/src/features/healing/HealingRepairPanel.tsx`
- [x] T081 [US3] Add a least-privilege reusable repository workflow template in `templates/healing/github/elsa-control-healing-repair.yml`

**Checkpoint**: User Story 3 passes reproduced, inferred, revision-unverified, insufficient-confidence, already-fixed, provider-retry, and malicious-patch scenarios with no agent access to Git credentials.

---

## Phase 6: User Story 4 — Merge Safe Repairs Under Repository Policy (Priority: P2)

**Goal**: Support human merge for every PR and narrowly gated auto-merge for fully reproduced, independently verified, low-risk repairs.

**Independent Test**: A complete negative gate matrix denies auto-merge for every failed/missing/stale/ambiguous/unknown condition; only the fully eligible opt-in case may request merge.

### Tests for User Story 4

- [x] T082 [P] [US4] Add failing complete publication/auto-merge gate matrix tests in `tests/ElsaControl.Healing.Core.Tests/Repairs/AutoMergeEligibilityPolicyTests.cs`
- [x] T083 [P] [US4] Add failing branch-protection/check refresh and idempotent merge-request tests in `tests/ElsaControl.Healing.GitHub.Tests/GitHubMergeProviderTests.cs`
- [x] T084 [P] [US4] Add failing dual-authorization retry/stop/evidence/waiver command tests in `tests/ElsaControl.Healing.Core.Tests/Providers/HumanProviderCommandPolicyTests.cs`
- [x] T085 [P] [US4] Add failing merge gate and command UI tests in `src/ElsaControl.Console/src/features/healing/HealingMergePolicyPanel.test.tsx`

### Implementation for User Story 4

- [x] T086 [P] [US4] Implement versioned path, evidence, publication, and auto-merge pure policies in `src/ElsaControl.Healing.Core/Repairs/HealingRepairPolicies.cs`
- [x] T087 [P] [US4] Implement provider check/branch-protection snapshots and merge requests in `src/ElsaControl.Healing.GitHub/GitHubMergeProvider.cs`
- [x] T088 [US4] Implement fresh deny-by-default merge evaluation and audit persistence in `src/ElsaControl.Healing.Core/Repairs/HealingMergeService.cs`
- [x] T089 [US4] Implement verified, identity-linked human provider commands and confirmations in `src/ElsaControl.Healing.Core/Providers/HumanProviderCommandService.cs`
- [x] T090 [US4] Extend verified webhook processing for PR/check/issue-command observations in `src/ElsaControl.Healing.GitHub/GitHubWebhookProcessor.cs`
- [x] T091 [US4] Implement merge policy gate matrix and human-command controls in `src/ElsaControl.Console/src/features/healing/HealingMergePolicyPanel.tsx`

**Checkpoint**: User Story 4 proves human merge remains available and auto-merge fails closed under every non-eligible combination.

---

## Phase 7: User Story 5 — Verify Healing After Deployment (Priority: P2)

**Goal**: Observe repaired revisions per environment and mark healed only after positive affected-operation evidence and a no-recurrence verification window.

**Independent Test**: One environment heals while another remains deployed-unverified; recurrence fails verification and emits a trusted signal without Elsa Control deployment/rollback.

### Tests for User Story 5

- [x] T092 [P] [US5] Add failing per-environment deployment/positive-operation/recurrence/window tests in `tests/ElsaControl.Healing.Core.Tests/Verification/HealingVerificationServiceTests.cs`
- [x] T093 [P] [US5] Add failing Elsa Control-managed and external idempotent deployment observation API tests in `tests/ElsaControl.Api.Tests/Healing/HealingDeploymentObservationApiTests.cs`
- [x] T094 [P] [US5] Add failing multi-environment verification UI tests in `src/ElsaControl.Console/src/features/healing/HealingVerificationPanel.test.tsx`

### Implementation for User Story 5

- [x] T095 [P] [US5] Implement trusted deployment observation append and Elsa Control deployment bridge in `src/ElsaControl.Healing.Core/Verification/DeploymentObservationService.cs`
- [x] T096 [US5] Implement per-environment positive verification, recurrence failure, supersession, waiver, and incident closure in `src/ElsaControl.Healing.Core/Verification/HealingVerificationService.cs`
- [x] T097 [US5] Implement due-window polling and trusted verification-failed contribution in `src/ElsaControl.Healing.Core/Verification/HealingVerificationWorker.cs`
- [x] T098 [US5] Map external deployment observation and verification waiver routes in `src/ElsaControl.Api/Workspace/Healing/HealingVerificationEndpoints.cs`
- [x] T099 [US5] Bridge successful Elsa Control deployment completion into Healing observations in `src/ElsaControl.Api/Workspace/Healing/ControlDeploymentHealingObserver.cs`
- [x] T100 [US5] Implement per-environment deployment and verification timeline in `src/ElsaControl.Console/src/features/healing/HealingVerificationPanel.tsx`

**Checkpoint**: User Story 5 distinguishes merged, deployed, deployed-unverified, healed, failed-verification, superseded, and waived states across multiple environments.

---

## Phase 8: User Story 6 — Review Healing Activity and Outcomes (Priority: P3)

**Goal**: Provide workspace-isolated overview, audit reconstruction, safe evidence metadata, outcomes, and usage without leaking protected data.

**Independent Test**: Successful, blocked, and failed-verification incidents are fully reconstructable by authorized users; unauthorized/cross-workspace users receive no protected details.

### Tests for User Story 6

- [x] T101 [P] [US6] Add failing audit reconstruction, safe projection, pagination, usage, and isolation tests in `tests/ElsaControl.Api.Tests/Healing/HealingAuditApiTests.cs`
- [x] T102 [P] [US6] Add failing overview/filter/audit/permission UI tests in `src/ElsaControl.Console/src/features/healing/HealingOverviewPage.test.tsx`

### Implementation for User Story 6

- [x] T103 [P] [US6] Implement safe overview, audit, and usage queries in `src/ElsaControl.Healing.Core/Reporting/HealingReportingService.cs`
- [x] T104 [US6] Map overview, audit, and usage endpoints with Healing permissions in `src/ElsaControl.Api/Workspace/Healing/HealingReportingEndpoints.cs`
- [x] T105 [US6] Implement Healing overview, filters, usage, and audit timeline in `src/ElsaControl.Console/src/features/healing/HealingOverviewPage.tsx`
- [x] T106 [US6] Add accessible loading/empty/error/stale states in `src/ElsaControl.Console/src/features/healing/HealingStateViews.tsx`

**Checkpoint**: User Story 6 reconstructs every automated decision while authorization and redaction tests prevent cross-workspace/protected-evidence disclosure.

---

## Phase 9: Polish, integration, self-review, and release

**Purpose**: Prove the full specification, harden operational behavior, and prepare coordinated PRs.

- [x] T107 [P] Add the 10,000-occurrence/100-instance deduplication performance scenario in `tests/ElsaControl.Healing.Persistence.EntityFrameworkCore.Tests/HealingScaleTests.cs`
- [x] T108 [P] Add cross-workspace, PII/token leak, webhook replay, OIDC substitution, malicious evidence, and publisher credential-isolation matrix in `tests/ElsaControl.Api.Tests/Healing/HealingSecurityMatrixTests.cs`
- [x] T109 [P] Add SQLite and SQL Server migration round-trip/parity tests in `tests/ElsaControl.Healing.Persistence.EntityFrameworkCore.Tests/HealingMigrationTests.cs`
- [x] T110 Add end-to-end fake-provider lifecycle coverage from OTLP through healed deployment in `tests/ElsaControl.Api.Tests/Healing/HealingEndToEndTests.cs`
- [x] T111 Validate the local Foundation packages against Elsa Control's combined OTLP-to-inbox integration in `tests/ElsaControl.Healing.OpenTelemetry.Tests/FoundationPackageIntegrationTests.cs`
- [x] T112 Update operator/customer setup and threat-model documentation in `docs/healing/getting-started.md` and `docs/healing/security.md`
- [ ] T113 Run all Foundation and Elsa Control .NET tests/builds, Console tests/typecheck/build, package pack tests, migration parity, and `git diff --check` from `specs/039-elsa-control-self-healing/quickstart.md`
- [ ] T114 Perform iterative architecture, correctness, security, API compatibility, test-quality, and UI self-review; record resolved findings in `specs/039-elsa-control-self-healing/review.md`
- [ ] T115 Run an independent diff review and repeat T113/T114 until no actionable findings remain, recording final evidence in `specs/039-elsa-control-self-healing/review.md`
- [ ] T116 Mark every completed task, reconcile `spec.md`/`plan.md`/contracts with implementation, and record requirement-by-requirement proof in `specs/039-elsa-control-self-healing/completion-audit.md`
- [x] T117 Commit and push the Foundation branch, open its PR, satisfy CI/review, merge it, and record the published package version in `specs/039-elsa-control-self-healing/completion-audit.md`
- [ ] T118 Update Elsa Control to the merged Foundation package, rerun the full cross-repository gate, commit and push Elsa Control, open its PR, satisfy CI/review, merge it, and record final state in `specs/039-elsa-control-self-healing/completion-audit.md`

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1** has no behavioral dependency.
- **Phase 2** depends on Phase 1 and blocks every user story.
- **US1** and **US2** can proceed after Phase 2; US2 repair eligibility consumes US1 manifests/bindings but remains testable with fixtures.
- **US3** depends on authoritative incidents from US2 and repair authority from US1.
- **US4** depends on US3 pull requests.
- **US5** depends on US3/US4 merged-revision observations but is independently testable with seeded repairs.
- **US6** reads all prior state but can develop against seeded aggregates after Phase 2.
- **Phase 9** depends on all six user stories.

### Within each user story

1. Write tests and confirm they fail for the intended missing behavior.
2. Implement contracts/models before services.
3. Implement services before endpoints/adapters.
4. Implement UI against the stable API contract.
5. Run the independent checkpoint before marking the phase complete.

### Parallel opportunities

- Foundation contributor tests/implementation can proceed alongside Elsa Control project scaffolding.
- In each story, core, adapter, API, and UI tests marked `[P]` target separate files.
- US1 manifest generation and ownership configuration can proceed in parallel until binding activation integration.
- US3 GitHub App, OIDC, agent gateway, and evidence services can proceed in parallel behind Abstractions.
- US5 verification and US6 reporting can proceed in parallel against seeded persistence fixtures.

## Implementation strategy

### First demonstrable increment

1. Complete setup and foundational phases.
2. Complete US1 configuration/ownership.
3. Complete US2 OTLP intake/deduplication.
4. Demonstrate durable exception-to-canonical-incident behavior before adding source mutation.

### Full product increment

1. Add governed draft PR production (US3).
2. Add human and gated automatic merge (US4).
3. Add deployment-based verification (US5).
4. Add audit/overview/usage (US6).
5. Complete the cross-repository security, scale, self-review, PR, and merge gates.

## Notes

- Never modify or clean the user's dirty primary Foundation worktree; all Foundation tasks use the isolated worktree named above.
- Commit logical, independently verified phases; do not stage unrelated changes.
- A checked task is a claim backed by test/build/current-state evidence, not merely an attempted edit.
- GitHub sandbox tests are opt-in and must use a dedicated repository/App installation.
