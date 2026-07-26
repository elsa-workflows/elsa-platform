# Self-Healing Completion Audit

**Feature**: 039 Valence Control Self-Healing
**Implementation status**: In progress; independent review findings and final Valence Control release gates pending
**Specification**: `spec.md`  
**Acceptance path**: `quickstart.md`

This audit maps every functional requirement and success criterion to its primary implementation and executable proof. Paths are repository-relative unless the Foundation repository is named explicitly.

## Functional requirements

| Requirement | Primary implementation | Executable proof |
| --- | --- | --- |
| FR-001 | `HealingConfigurationService`, workspace configuration endpoints, environment overrides | `WorkspaceHealingConfigurationApiTests`, `HealingConfigurationPage.test.tsx` |
| FR-002 | Foundation OTLP contributor bridge plus explicit incident endpoint/client | `FoundationPackageIntegrationTests`, `HealingIntakeApiTests`, `HealingClientTests` |
| FR-003 | Versioned contracts in `HealingContracts.cs`; client-independent normalizer | `HealingBoundaryTests`, `HealingSignalClassifierTests` |
| FR-004 | Component manifest contract, canonicalizer, generator, registration service | Component manifest and generator test projects |
| FR-005 | Manifest trust state and revision-bound attribution/merge gates | `SourceOwnershipServiceTests`, `AutoMergeEligibilityPolicyTests` |
| FR-006 | Source ownership bindings and path/evidence/merge policies | `SourceOwnershipServiceTests`, configuration API/UI tests |
| FR-007 | Non-authoritative metadata suggestions plus owner/provider activation | `SourceOwnershipServiceTests`, `WorkspaceHealingConfigurationApiTests` |
| FR-008 | Deterministic overlap and ambiguity resolution | `SourceOwnershipServiceTests`, authority API tests |
| FR-009 | Separate package-feed and provider/source authority models | `HealingBoundaryTests`, ownership tests |
| FR-010 | Server-owned route scope and authority configuration; no routing fields in signals | `HealingBoundaryTests`, `HealingIntakeApiTests` |
| FR-011 | Post-redaction Foundation contributor and explicit redaction enforcement | Foundation ingestion tests, OpenTelemetry contributor tests, client tests |
| FR-012 | Curated classifier with versioned overrides | `HealingSignalClassifierTests` |
| FR-013 | Idempotent durable inbox and leased worker | OpenTelemetry contributor, intake API, persistence, and inbox worker tests |
| FR-014 | Valence Control incident/episode/work-item projections are canonical | Incident projection, webhook, and end-to-end tests |
| FR-015 | Stable fingerprint service excluding volatile attributes | `HealingSignalClassifierTests` |
| FR-016 | Similarity does not merge incident identities | Fingerprint and incident service tests |
| FR-017 | Filtered unique incident/work-item indexes and idempotent provider operations | Concurrency/scale, persistence, and end-to-end tests |
| FR-018 | Incident, episode, occurrence, attribution, and environment-impact projections | `IncidentProjectionConcurrencyTests`, `HealingScaleTests` |
| FR-019 | Classification policy threshold and debounce fields | Classifier and incident service tests |
| FR-020 | Linked regression episodes with immutable prior history | Incident projection and verification tests |
| FR-021 | Manifest/frame/package/assembly attribution with candidate outcomes | Component attribution and ownership tests |
| FR-022 | Selected active binding and confidence gate before orchestration | Incident and repair orchestration tests |
| FR-023 | Observation-only incident states remain queryable | Classifier, incident API, and console tests |
| FR-024 | Revision-unverified high-confidence draft classification | Repair orchestration, coordinator, and UI tests |
| FR-025 | Producing-revision inspection and current target-branch proposal | Agent gateway and coordinator tests |
| FR-026 | Target inspection supports `AlreadyFixed` without duplicate repair | `RepairOrchestrationTests` |
| FR-027 | Provider-neutral orchestration contracts with GitHub v1 adapters | Boundary, provider operation, and GitHub tests |
| FR-028 | Idempotent machine-owned work-item upsert | `GitHubRepairProviderTests`, provider handler tests |
| FR-029 | Active binding, provider connection, immutable workflow and repository validation | Authority, provider handler, and security matrix tests |
| FR-030 | GitHub OIDC workload exchange with scoped capabilities | `GitHubWorkloadIdentityValidatorTests`, workflow API tests |
| FR-031 | Prompt/source sanitization and no-tools managed inference seam | `CopilotRepairProposalProviderTests`, security matrix |
| FR-032 | Bounded redacted evidence and audited elevation decisions | `RepairOrchestrationTests`, evidence API/security tests |
| FR-033 | Managed Copilot proposal provider; repository execution only in approved workflow | Agent provider tests and workflow template |
| FR-034 | Capability protocol excludes provider write credentials | Boundary, agent, workflow, and security matrix tests |
| FR-035 | Inert proposal separated from trusted patch publisher | Agent, trusted publisher, and proposal-binding tests |
| FR-036 | Self-protection path/category rules | `TrustedPatchPublisherTests`, security matrix |
| FR-037 | Reproduced/inferred/insufficient/revision-unverified classifications persisted and displayed | Orchestration, publication, API, and console tests |
| FR-038 | Policy-controlled inferred draft; merge policy denies automatic merge | Orchestration and auto-merge policy tests |
| FR-039 | Transactional per-episode attempt cap of at most two | Repair orchestration, persistence concurrency, coordinator tests |
| FR-040 | Provider identity plus Valence Control permission and confirmation policy | Human command policy, webhook, and API tests |
| FR-041 | PRs remain human-mergeable independently of Valence Control auto-merge | Merge policy and console tests |
| FR-042 | Explicit repository/application automatic-merge configuration | Configuration API and merge policy tests |
| FR-043 | Complete required-check, verifier, reproduction, revision, regression, risk, and rollout gate set | `AutoMergeEligibilityPolicyTests`, security matrix |
| FR-044 | Failed/missing/stale/ambiguous/unknown gates deny with reasons | Auto-merge negative matrix and audit API tests |
| FR-045 | Sensitive categories and non-reproduced/revision-unverified repairs deny auto-merge | Auto-merge policy and publisher tests |
| FR-046 | Fresh provider branch protection/check snapshot revalidated before merge | `GitHubMergeProviderTests`, merge crash-recovery tests |
| FR-047 | Valence Control and external trusted deployment observations | Deployment observation service/API tests |
| FR-048 | Valence Control only observes deployment; no deployment/rollback authority is injected | Boundary and deployment observation tests |
| FR-049 | Verification records keyed per episode/environment/revision | Verification service and persistence tests |
| FR-050 | Deployment, positive affected operation, elapsed window, and no recurrence gates | `HealingVerificationServiceTests` |
| FR-051 | Explicit merge/deploy/verification outcome enums and projections | Verification API/UI tests |
| FR-052 | Incident closure aggregates every active environment | Multi-environment verification tests |
| FR-053 | Trusted recurrence signal sink without rollback action | Verification worker and end-to-end tests |
| FR-054 | Workspace/application keys and authorization at every store/API/provider boundary | `HealingSecurityMatrixTests`, audit isolation tests |
| FR-055 | Redaction, bounded safe contracts, prompt sanitizer, secret-path exclusions | Foundation redaction tests, agent/client/security matrix tests |
| FR-056 | Append-only audit service and immutable persistence guard | Audit service, DbContext, reporting API tests |
| FR-057 | Valence Control/workspace/application/environment stops | Configuration, coordinator, and API tests |
| FR-058 | Time, concurrency, inference, repository-run, and attempt budgets | Options, orchestration, atomic admission, usage tests |
| FR-059 | Overview, configuration, incident, audit, usage, policy, and verification console surfaces | Six Healing console suites (30 tests) |
| FR-060 | Independent discovery/review/dispatch/merge/verification host controls | Options validation, composition, and configuration tests |

## Success criteria

| Criterion | Evidence and result |
| --- | --- |
| SC-001 | Getting-started flow and configuration UI cover source registration, manifest trust, provider validation, binding approval, and staged enablement without source edits. UI/API acceptance tests pass. |
| SC-002 | Durable acknowledgement precedes background processing; leased inbox workers run continuously with indexed pending work. Deterministic intake/worker tests pass. Production latency remains an operational SLO measured by telemetry. |
| SC-003 | `HealingScaleTests` sends 10,000 occurrences across 100 instances, 10 revisions, and four environments and asserts one canonical incident/work item with all impact preserved. |
| SC-004 | Ownership ambiguity/authorization tests and the security matrix deny every tested mutation and preserve reason codes. |
| SC-005 | Publication contracts and repair panel tests assert incident, attempt, tier, revision status, validation, classification, and risk metadata. |
| SC-006 | The full auto-merge negative matrix passes for failed, missing, stale, ambiguous, and unknown gates. |
| SC-007 | Foundation, agent prompt, evidence, work-item, PR, audit, and API security tests find no raw restricted data. Production dependency audit reports zero runtime vulnerabilities. |
| SC-008 | End-to-end and workflow capability tests complete retrieval, proposal, publication, and PR creation without placing provider write credentials in the agent capability. |
| SC-009 | Verification tests prove no healed state without repaired deployment, positive relevant execution, elapsed window, and no recurrence. |
| SC-010 | Multi-environment tests keep the incident open until each environment is healed, superseded, or waived. |
| SC-011 | Attempt-cap and atomic admission tests stop default automation after two attempts and retain safe evidence/status for operators. |
| SC-012 | Audit reconstruction/pagination tests cover automated decisions; unauthorized and cross-workspace access is denied. |

## Cross-repository state

### Elsa Foundation

- PR: [elsa-workflows/elsa-foundation#692](https://github.com/elsa-workflows/elsa-foundation/pull/692)
- State: merged on 2026-07-16
- Merge commit: `92de20a21b5e0f821d8add8f3ec954aa7fd27402`
- Published coordinated packages used by Valence Control: `Elsa.Diagnostics.OpenTelemetry` and `Elsa.Diagnostics.OpenTelemetry.Core` `4.0.0-preview.167`
- Validation: OpenTelemetry 71/71 tests; Foundation solution build succeeded; both packages packed successfully.

### Valence Control

- Branch: `039-valence-control-self-healing`
- Foundation package references: `4.0.0-preview.167`
- PR: pending final gate and review
- Merge state: pending

## Validation ledger

| Gate | Result |
| --- | --- |
| Valence Control solution build | Passed, 0 warnings / 0 errors |
| Valence Control full .NET suite | Final rerun pending after review corrections |
| Healing console tests | Passed, 30/30 |
| Console typecheck | Passed |
| Console production build | Passed; existing bundle-size/dependency annotation warnings only |
| SQLite/SQL Server migration parity | Passed |
| 10,000-occurrence scale scenario | Passed |
| Runtime dependency audit | `npm audit --omit=dev`: 0 vulnerabilities; .NET vulnerable-package scan: none |
| Diff whitespace check | Passed |
| Independent final review | Pending rerun |

The real GitHub sandbox procedure is intentionally credentialed and opt-in. No dedicated sandbox authority is present in this repository or test environment; it remains a mandatory environment enablement gate before real automatic merge is switched on, as documented in `quickstart.md` and `review.md`.
