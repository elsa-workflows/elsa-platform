# Feature Specification: Platform Self-Healing

**Feature Branch**: `039-platform-self-healing`

**Created**: 2026-07-16

**Status**: In progress — independent review and release validation

**Input**: User description: "Productize Elsa Platform Healing for .NET applications: receive eligible exceptions through the Platform OpenTelemetry capability, deduplicate incidents, attribute failures to explicitly approved source ownership bindings, dispatch governed repairs to GitHub-hosted repository workflows, open and optionally auto-merge safe pull requests, and verify healing after deployment."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Repairable Application Components (Priority: P1)

A workspace owner enables Healing for an application, registers its environments, supplies a revision-bound component manifest, connects an authorized source repository, and chooses which application components are eligible for monitoring, repair, and optional auto-merge.

**Why this priority**: Healing must know exactly which components the customer owns and which repository is authorized before it can safely turn runtime evidence into source changes.

**Independent Test**: Can be tested by registering an application revision containing first-party and third-party packages, approving one source ownership binding, and verifying that only the approved components become repairable while ambiguous or unapproved components remain observation-only.

**Acceptance Scenarios**:

1. **Given** an application revision with a trusted component manifest, **When** a workspace owner approves a package or assembly selector and its repair repository, **Then** Platform shows the selected components as repairable under that binding.
2. **Given** package metadata suggests a source repository, **When** no workspace owner has confirmed that repository, **Then** Platform presents the suggestion but grants no mutation authority.
3. **Given** two active ownership bindings match the same component but point to different repair authorities, **When** Platform evaluates the configuration, **Then** it blocks automated repair until the conflict is explicitly resolved.
4. **Given** Healing is disabled for an application or environment, **When** eligible exception telemetry arrives, **Then** the telemetry remains available through observability features but no Healing incident or repair run is created.

---

### User Story 2 - Turn Runtime Exceptions Into Deduplicated Incidents (Priority: P1)

A developer or operations engineer enables automatic exception discovery for a monitored application. Platform observes qualifying, redacted exception signals, groups repeated occurrences across instances and environments, and presents one canonical incident with current impact, evidence, attribution confidence, and repair eligibility.

**Why this priority**: Reliable collection and deduplication are the foundation of the product; without them, repair automation produces duplicate work, alert storms, and unsafe attribution.

**Independent Test**: Can be tested by submitting repeated occurrences of one exception fingerprint from multiple instances, revisions, and environments and verifying that Platform creates one active incident, preserves every occurrence, applies the configured threshold, and does not convert expected failures into repair work.

**Acceptance Scenarios**:

1. **Given** repeated qualifying exception occurrences share a deterministic fingerprint and repair repository, **When** the configured threshold is reached, **Then** Platform creates one active incident and one repair work item projection.
2. **Given** fatal startup failure, explicit Healing incident, unexpected request failure, and exhausted transient failure classifications, **When** occurrences arrive, **Then** Platform applies the configured classification-specific threshold to each type.
3. **Given** an expected validation, authorization, cancellation, handled, or still-retrying failure, **When** its telemetry arrives, **Then** Platform excludes it from automatic repair unless an authorized application classification overrides the default.
4. **Given** an incident was resolved and the same fingerprint later recurs on a subsequent revision, **When** Platform processes the new occurrence, **Then** it creates a linked regression episode instead of rewriting the completed repair history.

---

### User Story 3 - Produce A Governed Repair Pull Request (Priority: P1)

A qualifying incident is assigned to an authorized repository repair workflow. The repository runner retrieves a bounded evidence bundle, analyzes or reproduces the failure, proposes a minimal patch, runs validation, and opens a traceable pull request through a trusted publisher.

**Why this priority**: A reviewable, evidence-backed pull request is the first complete customer outcome and proves that observability, attribution, agent execution, source control, and safety policy work together.

**Independent Test**: Can be tested with a seeded exception whose source component and repair repository are known, verifying that the resulting pull request links the incident and producing revision, states its evidence tier, includes validation results, and cannot modify forbidden guardrails.

**Acceptance Scenarios**:

1. **Given** a reproduced failure on a verified producing revision, **When** the repair workflow succeeds, **Then** the pull request records reproduction evidence, the regression check, patch summary, risks, rollback guidance, and validation outcome.
2. **Given** the agent cannot reproduce the failure but has high-confidence causal evidence, **When** policy permits an inferred repair, **Then** it may open a clearly marked unreproduced draft pull request that requires human merge.
3. **Given** the producing revision is unknown but the repository is known, **When** analysis reaches high confidence, **Then** it may open a revision-unverified draft pull request that requires human merge.
4. **Given** the defect is already fixed on the current target branch, **When** the workflow compares the producing and target revisions, **Then** it links the fixing change and waits for deployment instead of opening a duplicate pull request.
5. **Given** a proposed patch changes the repair workflow, publisher, permission policy, or validation guardrails, **When** publication is evaluated, **Then** the patch is rejected and the incident requires human intervention.

---

### User Story 4 - Merge Safe Repairs Under Repository Policy (Priority: P2)

A repository owner chooses whether Healing pull requests always require human merge or may auto-merge under a narrow, explicit low-risk policy. Platform and the repository enforce every configured gate before any automatic merge occurs.

**Why this priority**: Human-reviewed pull requests deliver value without autonomous merge, while carefully constrained auto-merge completes the intended self-healing experience for proven low-risk cases.

**Independent Test**: Can be tested with a matrix containing reproduced and unreproduced failures, verified and unverified revisions, normal and sensitive paths, passing and failing checks, and enabled and disabled repository policies; only the single fully eligible combination may auto-merge.

**Acceptance Scenarios**:

1. **Given** auto-merge is disabled for the repository, **When** all repair checks pass, **Then** the pull request remains ready for an authorized human decision.
2. **Given** auto-merge is enabled and every required low-risk gate passes, **When** repository policy permits merge, **Then** the repair may merge without a separate human action.
3. **Given** any required auto-merge gate fails or is unknown, **When** merge eligibility is evaluated, **Then** automatic merge is denied and the exact blocking reason is recorded.
4. **Given** an authorized maintainer requests retry, stop, or evidence elevation from the Git work item, **When** Platform validates the actor and command, **Then** it records and applies the command without treating labels or comments as canonical state.

---

### User Story 5 - Verify Healing After Deployment (Priority: P2)

After a repair is merged, Platform observes the repaired revision being deployed to each affected environment and confirms that the affected operation executes successfully without the incident recurring. Only then does Platform mark that environment and eventually the incident as healed.

**Why this priority**: A pull request is an attempted repair, not proof of recovery. Positive post-deployment evidence closes the product loop and distinguishes self-healing from automated code generation.

**Independent Test**: Can be tested by reporting deployment of a repaired revision, sending positive operation evidence and no recurrence for one environment while withholding it for another, and verifying that only the first environment becomes healed and the overall incident stays open.

**Acceptance Scenarios**:

1. **Given** a repaired revision is deployed, **When** the affected operation succeeds during the verification window and the fingerprint does not recur, **Then** Platform marks that environment healed.
2. **Given** the fingerprint does not recur but no relevant operation executes, **When** the verification window ends, **Then** Platform records the environment as deployed but unverified rather than healed.
3. **Given** the fingerprint recurs during verification, **When** Platform evaluates the repair, **Then** it records failure evidence, keeps the incident open, and emits a trusted failure signal for the deployment system.
4. **Given** different affected environments deploy at different times, **When** verification progresses, **Then** Platform tracks each environment independently and closes the incident only after all active affected environments are verified, superseded, or explicitly waived.

---

### User Story 6 - Review Healing Activity And Outcomes (Priority: P3)

Developers, operations engineers, security reviewers, and auditors use the Platform console to review incidents, component ownership, evidence access, repair attempts, pull requests, merge decisions, deployment verification, failures, and usage without exposing raw sensitive data.

**Why this priority**: Autonomous and agent-assisted changes require durable accountability, understandable decisions, and measurable outcomes before customers can trust or expand their policies.

**Independent Test**: Can be tested by completing one successful repair, one blocked repair, and one failed verification, then verifying that authorized users can reconstruct every relevant decision while unauthorized users cannot access another workspace's data or raw elevated evidence.

**Acceptance Scenarios**:

1. **Given** an authorized workspace member opens Healing, **When** they inspect an incident, **Then** they can see occurrence impact, attribution, evidence tier, work item, attempts, pull requests, merge state, and per-environment verification.
2. **Given** an auditor inspects an auto-merged repair, **When** they review its history, **Then** every eligibility gate, agent action, publisher decision, repository check, deployment observation, and verification outcome is attributable and timestamped.
3. **Given** a user lacks workspace or evidence-elevation permission, **When** they request Healing data, **Then** Platform denies access without revealing protected incident or repository details.

### Edge Cases

- Duplicate, delayed, retried, or out-of-order occurrences arrive from multiple replicas.
- The telemetry module accepts a signal, but incident projection is temporarily unavailable.
- The application supplies an occurrence ID that was already accepted.
- Exception messages, stack traces, logs, source comments, issue content, or test fixtures contain adversarial instructions.
- Redaction removes data the agent would have found useful.
- A component manifest is missing, stale, unsigned, mismatched with the reported revision, or references an unknown package version.
- Package metadata suggests a repository that the workspace has not authorized.
- Multiple packages in one call stack have plausible causal frames or map to different repositories.
- The producing revision no longer exists, the default branch moved, or the proposed patch is stale before publication.
- The target branch already contains a fix or has changed enough that the original failure no longer applies.
- The Git provider is unavailable, rate-limited, disconnected, or no longer installed for the repository.
- A user manually closes the projected work item, removes labels, edits machine-managed content, or deletes the repair branch.
- The repair agent times out, exceeds budget, produces no patch, changes forbidden paths, or returns malformed evidence.
- Build or test commands execute hostile repository code and attempt to access credentials.
- A repair cannot be reproduced because it depends on production-only data, concurrency, timing, or an unavailable service.
- The same incident reaches the automatic-attempt limit.
- Required checks pass, but a protected-branch or designated-owner policy still blocks merge.
- A merge triggers deployment outside Platform, but no trusted deployment observation is reported.
- A deployed fix receives no relevant traffic during the verification window.
- The same repair succeeds in one environment and fails in another.
- A resolved incident recurs on a later revision or under a materially different causal stack.
- A source ownership binding is revoked while a repair is active.
- A workspace disables Healing or activates an emergency kill switch during collection, repair, merge, or verification.

## Requirements *(mandatory)*

### Functional Requirements

#### Configuration And Ownership

- **FR-001**: System MUST allow authorized workspace owners to enable or disable Healing independently for each application and environment.
- **FR-002**: System MUST require the Platform telemetry capability for automatic exception discovery while retaining explicit incident reporting for domain-specific failures and product testing.
- **FR-003**: System MUST accept a versioned Healing Signal Profile whose conformance is independent of any specific client library.
- **FR-004**: System MUST allow a supported application revision to register an immutable component manifest containing the component identities, versions, assemblies, content hashes, dependency relationships, and available source metadata needed for attribution.
- **FR-005**: System MUST require a trusted revision-bound component manifest before a component is eligible for fully automated repair or auto-merge.
- **FR-006**: System MUST allow workspace owners to create source ownership bindings using package, assembly, or application-component selectors and associate them with one approved repair authority, target branch, permitted repair workflow, path policy, and merge policy.
- **FR-007**: System MAY suggest source ownership bindings from package and source metadata, but MUST require explicit workspace-owner approval and source-provider authorization before granting mutation authority.
- **FR-008**: System MUST block automated repair when active ownership rules ambiguously resolve a component to different repair authorities unless an authorized owner records an explicit resolution.
- **FR-009**: System MUST keep package-feed identity distinct from source-code repair authority.
- **FR-010**: System MUST store repository, workflow, and repair-policy bindings as Platform-owned workspace configuration rather than accepting routing instructions from monitored applications.

#### Incident Intake And Deduplication

- **FR-011**: System MUST derive Healing candidates only from post-redaction telemetry or authorized explicit incident reports.
- **FR-012**: System MUST classify unhandled request failures, fatal startup or background failures, unexpected workflow or activity faults, and explicit incident reports as eligible by default, while excluding expected validation, authorization, cancellation, handled, and in-progress retry failures unless authorized policy overrides the classification.
- **FR-013**: System MUST durably and idempotently project eligible telemetry into incident intake so temporary Healing unavailability does not require the telemetry sender to resubmit the original signal.
- **FR-014**: System MUST keep Platform incidents authoritative over external work items, labels, comments, agent-local state, and repository workflow state.
- **FR-015**: System MUST compute a deterministic primary fingerprint from stable causal evidence, excluding volatile identifiers and environment-specific values.
- **FR-016**: System MAY suggest semantic relationships between incidents, but MUST NOT automatically merge canonical incidents based solely on agent or similarity analysis.
- **FR-017**: System MUST maintain at most one active incident and one active repair work item per fingerprint and repair repository while preserving all contributing occurrences.
- **FR-018**: System MUST aggregate environment, application revision, component, first-seen, last-seen, and occurrence-count impact on the canonical incident.
- **FR-019**: System MUST support classification-specific occurrence thresholds and a configurable debounce window before repair work is submitted.
- **FR-020**: System MUST create a linked regression episode when a resolved fingerprint recurs on a later revision rather than reopening or overwriting completed repair history.

#### Source Attribution And Repair Eligibility

- **FR-021**: System MUST attribute each candidate incident to zero, one, or multiple component candidates using the producing revision's component manifest and causal runtime evidence.
- **FR-022**: System MUST start automated repair only when one approved source ownership binding is selected with sufficient policy-defined attribution confidence.
- **FR-023**: System MUST retain incidents that lack a repairable component or authorized source binding as observation and human-triage records without mutating any repository.
- **FR-024**: System MUST treat the exact producing revision as preferred evidence but MAY allow a high-confidence, revision-unverified draft repair when the repository is known and policy permits it.
- **FR-025**: System MUST analyze or attempt reproduction against the producing revision when available, then create any repair against the current configured target branch.
- **FR-026**: System MUST avoid opening a duplicate repair when the target branch already contains a change that resolves the incident and MUST instead track deployment of the existing fix.

#### Governed Repair Execution

- **FR-027**: System MUST expose provider-neutral repair-work orchestration while using GitHub as the sole source-control and repository-workflow provider in v1.
- **FR-028**: System MUST create or update a provider work item for each submitted repair and maintain one machine-owned summary containing current impact and state without posting one comment per occurrence.
- **FR-029**: System MUST dispatch repository repair work only through the workspace's approved source-provider installation and trusted repository workflow.
- **FR-030**: System MUST require a repository workflow to authenticate to Platform with short-lived workload identity constrained to the approved repository, workflow, and incident.
- **FR-031**: System MUST treat work-item text, comments, telemetry, source files, test fixtures, and generated output as untrusted evidence rather than executable agent instructions.
- **FR-032**: System MUST provide repair agents only a bounded, redacted evidence bundle by default and require an authorized, auditable decision before releasing additional protected evidence.
- **FR-033**: System MUST provide managed, provider-neutral agent inference as the default product capability while allowing repository code, builds, and tests to execute only on repository-owned runners in v1.
- **FR-034**: System MUST ensure the repair agent cannot access source-provider write credentials.
- **FR-035**: System MUST separate agent patch production from a trusted publisher that validates the target revision, changed paths, change size, evidence, and required checks before creating or updating a repair branch and pull request.
- **FR-036**: System MUST prohibit automated changes to the Healing workflow, patch publisher, authorization policy, validation rules, or other self-protecting guardrails.
- **FR-037**: System MUST classify each repair as reproduced, inferred with high confidence, insufficient confidence, or revision-unverified and display the classification on the work item and pull request.
- **FR-038**: System MUST permit an unreproduced repair pull request only when policy allows high-confidence inference and MUST require human merge for that repair.
- **FR-039**: System MUST stop automatic repair after two failed attempts for the same incident episode and target revision by default, mark the incident as needing human attention, and prevent unlimited retry policies.
- **FR-040**: System MUST validate the repository role and workspace authorization of any human command received through a provider work item before applying retry, stop, evidence, or waiver decisions.

#### Merge Governance

- **FR-041**: System MUST support human-controlled merge for every repair pull request.
- **FR-042**: System MUST allow a repository owner to opt into automatic merge only through explicit repository policy.
- **FR-043**: System MUST require every configured auto-merge gate to pass, including verified producing revision, reproduced failure, before-and-after regression evidence, independent verification, required repository checks, low-risk path and size policy, absence of sensitive change categories, and availability of a trusted rollout-stop or rollback policy.
- **FR-044**: System MUST deny automatic merge when any required gate is failed, missing, stale, ambiguous, or unknown and MUST record each blocking reason.
- **FR-045**: System MUST prohibit automatic merge for unreproduced, revision-unverified, sensitive-path, public-contract, schema, dependency, authentication, secret, infrastructure, deployment, or self-protection changes.
- **FR-046**: System MUST defer to source-provider branch protection, designated-owner review, and repository merge policy even when Platform's own auto-merge gates pass.

#### Deployment Verification And Closure

- **FR-047**: System MUST accept trusted deployment observations that identify application, environment, revision, and deployment time whether deployment is managed by Platform or an external delivery system.
- **FR-048**: System MUST observe rather than initiate application deployment or rollback.
- **FR-049**: System MUST track repair verification independently for every active affected environment.
- **FR-050**: System MUST require observation of the repaired revision, a configurable verification window, at least one successful execution of the affected operation, and no matching recurrence before marking an environment healed.
- **FR-051**: System MUST distinguish merged, deployed, deployed-unverified, healed, failed-verification, superseded, and waived outcomes.
- **FR-052**: System MUST keep an incident open until every active affected environment is healed, superseded, or explicitly waived by an authorized operator.
- **FR-053**: System MUST emit a trusted repair-verification-failed signal when a repaired deployment recurs, while leaving rollout-stop and rollback authority to the deployment system.

#### Security, Audit, And Product Operation

- **FR-054**: System MUST enforce workspace and organization isolation for applications, telemetry-derived incidents, component manifests, source bindings, evidence, provider connections, work items, repair attempts, and verification records.
- **FR-055**: System MUST prevent raw secrets, provider credentials, tokens, connection strings, protected tenant payloads, and unrestricted request or workflow inputs from reaching work items, agents, pull requests, audit summaries, or unauthorized users.
- **FR-056**: System MUST record append-only audit evidence for configuration changes, candidate classification, deduplication, attribution, work-item projection, evidence access, agent activity, publication, merge eligibility, human commands, deployment observations, verification, failure, and closure.
- **FR-057**: System MUST provide application, workspace, and platform kill switches that stop new repair work and automatic merge without deleting incident history.
- **FR-058**: System MUST enforce configurable time, concurrency, inference-usage, repository-run, and repair-attempt budgets.
- **FR-059**: System MUST show authorized users an application-level Healing overview, components and source ownership, policies, incidents, integrations, audit history, usage, and per-environment verification state.
- **FR-060**: System MUST keep automatic discovery, incident review, repair dispatch, merge policy, and verification independently disableable so customers can adopt progressively.

### Key Entities *(include if feature involves data)*

- **Healing Configuration**: Workspace-owned application and environment settings controlling discovery, thresholds, repair eligibility, merge policy, budgets, and kill switches.
- **Healing Signal Profile**: Versioned behavioral contract describing eligible incident events, stable identity, classification, component inventory references, redaction expectations, and delivery semantics.
- **Component Manifest**: Immutable inventory for one application revision containing component, package, assembly, version, dependency, content-hash, and source metadata.
- **Source Ownership Binding**: Approved mapping from application, package, or assembly selectors to one repair authority, provider connection, repository, target branch, workflow, path policy, and merge policy.
- **Incident Occurrence**: One observed qualifying failure with application, environment, revision, timestamp, fingerprint evidence, trace correlation, and redaction metadata.
- **Healing Incident**: Canonical active or historical problem that aggregates matching occurrences, affected environments, attribution, severity, eligibility, and current repair state.
- **Incident Episode**: One bounded period in which a fingerprint is active, repaired, verified, failed, or later recurs as a linked regression.
- **Repair Work Item Projection**: Provider-hosted issue or equivalent work surface linked to the canonical incident and synchronized with material state transitions.
- **Evidence Bundle**: Bounded, redacted evidence released to one repair attempt, including its tier, provenance, access decision, and omitted-data markers.
- **Repair Attempt**: One authorized agent run against an incident episode and target revision, with budget, evidence, reproduction result, patch result, validation, and outcome.
- **Repair Pull Request**: Provider-hosted proposed source change linked to its incident, attempt, evidence tier, revision, checks, risks, and merge status.
- **Deployment Observation**: Trusted report that a specific application revision was deployed to an environment at a known time.
- **Verification Result**: Per-environment evaluation of deployment presence, relevant successful execution, recurrence, verification window, outcome, and supporting evidence.
- **Provider Connection**: Workspace-authorized relationship to a source-control provider installation, repositories, trusted workflows, and permitted operations.
- **Healing Audit Event**: Append-only safe record of one configuration, security, orchestration, evidence, source-control, merge, deployment, or verification decision.

### Scope Boundaries

- V1 supports .NET and ASP.NET Core applications, NuGet component identities, .NET assembly attribution, SourceLink-compatible metadata, and build-generated component manifests.
- V1 supports GitHub-hosted repositories and repository-owned GitHub Actions runners only, while core repair orchestration remains provider-neutral.
- Automatic exception discovery requires the Platform telemetry module. External observability-backend polling and connectors are excluded.
- The Healing Client is an optional Platform-owned reference implementation of the signal profile and explicit incident reporting, not a required application dependency.
- Platform-managed build or test runners are excluded from v1.
- Unconfigured third-party repositories and repositories without an approved ownership binding are observation-only.
- Healing does not deploy applications, execute rollback, change source-provider protections, or bypass repository policy.
- Java, JavaScript, Python, and other application build ecosystems are deferred.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workspace owner can enable Healing, upload or select a component manifest, approve one source ownership binding, and validate the Git provider connection in under 15 minutes without reading source code.
- **SC-002**: At least 99% of qualifying exception occurrences accepted under normal operating conditions become visible on their canonical incident within two minutes.
- **SC-003**: A validation run containing 10,000 duplicate occurrences across at least 100 application instances, multiple revisions, and multiple environments produces exactly one active incident and one active repair work item per fingerprint and repair repository.
- **SC-004**: All tested ambiguous or unauthorized ownership configurations are blocked from repository mutation, with an actionable reason visible to the workspace owner.
- **SC-005**: Every automatically produced pull request links one canonical incident, one repair attempt, its evidence tier, producing revision status, validation outcome, and risk summary.
- **SC-006**: A negative auto-merge test matrix proves automatic merge is denied whenever any required eligibility gate is failed, missing, stale, ambiguous, or unknown.
- **SC-007**: Security validation finds zero raw secrets, provider credentials, protected tenant payloads, or unredacted restricted evidence in agent inputs, work items, pull requests, or ordinary audit views.
- **SC-008**: A repository workflow can complete authenticated incident retrieval, agent analysis, patch publication, and pull-request creation without exposing source-provider write credentials to the repair agent.
- **SC-009**: Platform never reports an environment as healed without observing the repaired revision, at least one relevant successful execution, completion of the configured verification window, and no matching recurrence.
- **SC-010**: A multi-environment repair remains open until every active affected environment is individually healed, superseded, or explicitly waived.
- **SC-011**: A failed repair episode stops automatic execution after the configured maximum of no more than two default attempts and presents the accumulated evidence for human follow-up.
- **SC-012**: Authorized users can reconstruct every automated merge and verification outcome from audit history, while cross-workspace and unauthorized evidence-access tests are denied in all tested scenarios.

## Assumptions

- Existing workspace, organization, application, environment, deployed-revision, identity, authorization, audit, and observability concepts remain the surrounding Platform context.
- The Platform telemetry capability is composed wherever automatic Healing discovery is enabled and exposes a durable post-redaction contribution path.
- Customers can configure their telemetry pipeline to send supported signals to Platform and can generate a trusted component manifest during build or release.
- The Platform-owned Healing client may simplify .NET instrumentation, manifest generation, enrichment, and explicit reporting, but equivalent conforming instrumentation is allowed.
- Customers install and authorize the Platform GitHub App only for repositories they want Platform to inspect or repair and install a trusted repository repair workflow supplied or approved by Platform.
- Platform supplies managed agent inference, while repository-owned runners supply source checkout, build, test, and repository-local execution.
- Repair work uses a provider-neutral contract even though GitHub is the only v1 adapter.
- Existing delivery systems can report a trusted application, environment, revision, and deployment time to Platform even when Platform did not perform the deployment.
- Repository and deployment owners retain final authority through branch protections, merge rules, rollout controls, and rollback policy.
- Pricing, commercial entitlements, and plan packaging may govern availability and usage but do not change the safety requirements in this specification.
