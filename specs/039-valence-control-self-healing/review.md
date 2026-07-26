# Self-Healing Implementation Review

**Review status**: Final review in progress  
**Scope**: Valence Control implementation plus the merged Elsa Foundation OpenTelemetry ingestion seam
**Review standard**: architecture, correctness, durability, security, API compatibility, test quality, UI behavior, and specification consistency

## Review rounds

### Root architecture and security review

The implementation was reviewed repeatedly while each user story was integrated. The following actionable findings were resolved before the final gate:

| Finding | Resolution | Regression proof |
| --- | --- | --- |
| Provider-operation payloads could act as a confused deputy if repository identity was not rebound to the operation's workspace-owned connection. | Every GitHub mutation validates workspace, application, provider connection, and immutable repository identity before calling the provider. | `HealingSecurityMatrixTests`, provider-operation handler tests. |
| Auto-merge could accept an empty required-check set or stale provider state. | Eligibility now requires non-empty current required checks, an independent verifier, matching head/base revisions, branch protection, and a fresh policy snapshot. | `AutoMergeEligibilityPolicyTests`, `GitHubMergeProviderTests`, `HealingSecurityMatrixTests`. |
| Webhook payload identifiers and control-shaped strings were insufficiently constrained. | Positive PR numbers, bounded revisions, event/action allow-lists, HMAC validation, replay binding, and safe normalized commands are enforced. | `GitHubWebhookProcessorTests`, `ControlHealingVerifiedWebhookHandlerTests`. |
| Retry and verification paths could use stale or cross-tenant records. | Attempt limits are rechecked in the application transaction; verification reads and writes bind workspace, application, episode, environment, and revision. | `RepairOrchestrationTests`, `HealingVerificationServiceTests`, API isolation tests. |
| Deployment evidence validation was too permissive. | Observations require trusted identity, bounded identifiers, idempotency binding, evidence digest, environment scope, and full replay equivalence. | `HealingDeploymentObservationApiTests`, `HealingEndToEndTests`. |
| Reporting could use an unbounded default interval. | Omitted windows now default to 366 days; future and overlong windows are rejected. | `HealingReportingServiceTests`, `HealingAuditApiTests`. |
| An agent prompt could include credential-shaped content from repository context. | Server-side prompt sanitization and workflow-side sensitive path/content exclusions were added. | `CopilotRepairProposalProviderTests`, workflow validation. |
| The publishable Client project had no reusable implementation. | Added profile enrichment, explicit redacted incident reporting, typed DI registration, safe bounded failures, and client tests/documentation. | `HealingClientTests`. |
| The specification still described Weaver and an older workload result protocol. | Plan and contracts now describe the Copilot provider seam and proposal creation/finalization capabilities implemented by the code. | Spec Kit consistency analysis; contract/boundary tests. |
| The repository workflow source collector admitted source types outside the .NET-only v1 scope. | Collection is restricted to C#, F#, VB, Razor, MSBuild, and solution files, with sensitive paths excluded. | Workflow template review and action syntax validation. |
| The report store materialized unbounded entity and aggregate-ID collections. | Overview metrics are database aggregates, recent incidents are capped at 20, usage is streamed with bounded memory, and audit scope remains a composable database query with server-side pagination. | `HealingReportingStoreTests`, `HealingAuditApiTests`. |
| Repeated explicit client calls mutated `HttpClient.BaseAddress`, which is invalid after the first send. | Requests now construct an absolute URI without mutating the shared client. | `HealingClientTests`. |

### Independent diff review

An independent review found five additional issues. All five were fixed and covered before the final gate:

| Independent finding | Resolution | Regression proof |
| --- | --- | --- |
| Application concurrency budgets were checked before the attempt-creation transaction, allowing multi-instance oversubscription. | Repair admission serializes on the durable application configuration row and counts/creates attempts in one serializable transaction. | Concurrent two-context SQLite regression and coordinator budget test. |
| A stale `RequestMerge` operation could overwrite a successful webhook transition or fail a superseded repair. | Terminal/superseded canonical states complete stale operations non-fatally; provider-terminal snapshots await the signed webhook rather than rolling state back. | Merge crash-window security regressions. |
| A crash between allowed merge evaluation and operation enqueue could strand a PR. | PR claim and durable merge enqueue are atomic; allowed-but-unclaimed evaluations recover after one lease duration. | Auto-merge recovery regressions. |
| Webhook authority resolution rejected a valid GitHub installation/repository shared by multiple Valence Control workspaces. | One verified delivery now fans out to every unambiguous workspace authority with equal immutable repository identity and constant-time-equal secrets, retaining tenant-scoped replay records. | Five multi-workspace handler regressions and endpoint tests. |
| Reporting loaded unbounded rows and audit aggregate identifiers. | Replaced with SQL aggregates, bounded projections, streaming usage, and composable audit subqueries. | Reporting store scale/pagination tests. |

### Full-gate correction

The first full solution run exposed one pre-existing test-fixture contradiction: tests globally enabled trusted-header login while two customer-authentication tests expected all login modes to be disabled. Those tests now explicitly disable trusted headers when asserting the no-login configuration. The complete customer-authentication suite passes after the correction.

## Security invariants rechecked

- Monitored applications submit only profile data; they cannot select repository or mutation authority.
- Agent inputs, repository content, work-item text, and webhooks remain untrusted data.
- The repair agent never receives a GitHub write credential; Valence Control's trusted publisher performs mutations.
- Workspace/application scope is rebound at every persistence and provider boundary.
- Automatic merge fails closed and is never available to inferred, unreproduced, revision-unverified, sensitive, or stale repairs.
- A merge is not healing; positive per-environment post-deployment verification is mandatory.
- Provider, inference, telemetry, and deployment failures retain durable idempotent state and safe audit evidence.
- Kill switches and bounded attempt, concurrency, inference, repository-run, and time budgets take precedence over automation.

## External sandbox lane

The deterministic fake-provider lane covers the entire lifecycle and security matrix. The opt-in real GitHub sandbox procedure in `quickstart.md` requires a dedicated repository, GitHub App installation, branch protection, and credentials; none are stored in this repository or available to the test process. It remains a mandatory environment-specific enablement gate before automatic merge is enabled against a real repository, not a reason to grant the ordinary test process provider credentials.

## Final disposition

The implementation has no knowingly waived code, security, or test finding. Final disposition becomes **approved** only after the post-review full gate and independent re-review recorded below both pass.

### Final evidence

Pending final full-gate rerun and independent re-review.
