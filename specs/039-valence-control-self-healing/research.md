# Research: Valence Control Self-Healing

## Decision 1: Foundation exposes a generic post-redaction contribution seam

**Decision**: Add `IOpenTelemetryIngestionContributor` to `Elsa.Diagnostics.OpenTelemetry.Core`. `OpenTelemetryIngestor` redacts once, awaits every contributor, then writes to the diagnostics store and publishes to the live feed.

**Rationale**: Valence Control must durably accept exception candidates without putting Healing concepts into Foundation. Awaiting the Valence Control contributor means an OTLP success response is not returned until its idempotent inbox append succeeds. Worker downtime then cannot lose accepted repair work.

**Alternatives considered**:

- Consuming `IOpenTelemetryLiveFeed`: rejected because it is explicitly volatile and has no resumable sequence.
- Polling the diagnostics store: rejected because the generic query contract has no durable committed cursor and the EF store accepts into a shedding channel.
- Reusing workflow post-commit outbox contracts: rejected because they are checkpoint-specific runtime state.

## Decision 2: Healing owns a durable inbox and leased workers

**Decision**: The OpenTelemetry bridge appends a normalized, redacted signal envelope to a Healing-owned inbox with a stable idempotency key. Background workers lease inbox entries, classify candidates, project incidents, dispatch repairs, and verify deployments.

**Rationale**: Intake remains fast and retry-safe while every later side effect is resumable. The existing deployment command lease and webhook-dispatch workers provide proven Valence Control conventions.

**Alternatives considered**:

- Classifying and dispatching inline in the OTLP request: rejected because provider/inference latency and failure would couple telemetry availability to repair execution.
- In-memory channels: rejected because restarts and multiple replicas would lose or duplicate work.

## Decision 3: Healing uses a dedicated persistence subsystem

**Decision**: Create `HealingDbContext`, provider-neutral EF stores, SQLite migrations, and SQL Server migrations under Healing-owned projects. The context may use the same connection string/physical database as other Valence Control subsystems but keeps a separate migration history.

**Rationale**: `CatalogDbContext` already owns catalog, accounts, deployment, runtime configuration, and Weaver state. Adding Healing would contradict the bounded-subsystem constitution and make independent evolution harder.

**Alternatives considered**:

- Extend `CatalogDbContext`: rejected despite being mechanically smaller because Package Catalog would become the persistence owner of another major subsystem.
- Separate physical database as a requirement: rejected because deployment topology should remain configurable; logical ownership does not require an extra server.

## Decision 4: Deterministic fingerprinting is authoritative

**Decision**: Compute a versioned fingerprint from normalized exception type, stable stack frames, operation identity, component candidates, and repair repository. Exclude messages, instance IDs, trace IDs, request IDs, line numbers where unstable, and environment-specific values. Explicit profile occurrence IDs provide idempotency; otherwise derive a stable occurrence key from trace/span identity, timestamp, resource identity, and normalized causal evidence.

**Rationale**: Foundation's OTLP parser generates local log record IDs on every retry, so those IDs cannot prevent duplicates. Semantic/AI grouping remains advisory and cannot merge canonical incidents.

**Alternatives considered**:

- Message plus stack hash: rejected because messages commonly contain volatile data.
- Agent-selected grouping: rejected because it is nondeterministic and unsafe as an authority boundary.

## Decision 5: The Healing Signal Profile extends standard OpenTelemetry semantics

**Decision**: Use standard `exception.type`, `exception.message`, `exception.stacktrace`, `error.type`, service/resource, trace, span, and severity semantics. Add versioned `valence.control.healing.*` attributes only for product-specific identity, classification, retry exhaustion, operation identity, application/environment/revision, component-manifest reference, occurrence ID, and explicit-report intent.

**Rationale**: Standard semantics keep ordinary instrumentation useful while a small versioned profile makes automatic repair deterministic. OpenTelemetry reserves exception attributes/events and defines stable exception-log conventions.

**Alternatives considered**:

- A proprietary exception endpoint as the main path: rejected because it would create a parallel observability pipeline.
- Requiring the Valence Control client: rejected because conformance is behavioral; the client is only a reference implementation.

**Primary references**:

- [OpenTelemetry semantic conventions](https://opentelemetry.io/docs/specs/otel/semantic-conventions/)
- [Exception semantic conventions for logs](https://opentelemetry.io/docs/specs/semconv/exceptions/exceptions-logs/)

## Decision 6: Component manifests are immutable build artifacts

**Decision**: A Valence Control-owned MSBuild package generates a canonical JSON manifest for one application revision from resolved NuGet assets and managed assemblies. It records component/package/assembly identity, versions, dependency edges, content hashes, available source metadata, build/source revision, and a manifest digest. Upload associates that digest with a Valence Control application revision.

**Rationale**: Runtime stack frames alone cannot prove which package version or repository produced code. Build metadata can do so without loading or executing customer assemblies.

**Alternatives considered**:

- Extend `elsa-package.json`: rejected because it describes one Elsa package's feature metadata, not an application's complete resolved component graph.
- Trust NuGet repository metadata as repair authority: rejected because package source and source mutation authority are different concepts.

## Decision 7: Source ownership binding is the mutation authority

**Decision**: Workspace owners approve selectors and exactly one repair authority containing provider connection, immutable repository identity, target branch, approved workflow identity, path policy, evidence policy, and merge policy. Metadata may propose values but never activates them.

**Rationale**: Customer applications can contain first-party, third-party, forked, or repackaged components. Only workspace-owned policy can grant repair authority.

**Alternatives considered**:

- Use the repository URL in package metadata automatically: rejected as an unsafe confused-deputy path.
- Choose the first matching rule: rejected because overlap must fail closed.

## Decision 8: GitHub App installation authentication owns mutations

**Decision**: A GitHub adapter mints short-lived installation access tokens just in time, narrowed to the selected repository and required permissions. Tokens are never persisted or returned to agents/runners.

**Rationale**: GitHub recommends installation authentication for app-attributed automation and permits narrowing repositories and permissions; installation tokens expire after one hour.

**Alternatives considered**:

- Personal access tokens: rejected because ownership, revocation, and scope are inappropriate for a multi-tenant product.
- Passing `GITHUB_TOKEN` or an installation token to the repair agent: rejected because repository content is untrusted and agent compromise would become source mutation.

**Primary reference**: [Generating a GitHub App installation access token](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-an-installation-access-token-for-a-github-app)

## Decision 9: GitHub Actions authenticates through incident-scoped OIDC exchange

**Decision**: Valence Control validates GitHub's issuer, Valence Control-specific audience, signature, expiry, JWT ID, immutable repository identity, repository/workflow claims, `job_workflow_ref`, ref, SHA, run ID/attempt, and a one-time incident attempt nonce. It returns a short-lived capability token limited to one attempt's evidence read, heartbeat, and result upload.

**Rationale**: GitHub Actions OIDC provides signed repository/workflow/run claims without storing a long-lived Valence Control secret in the repository. `id-token: write` grants only token issuance, not repository write access.

**Alternatives considered**:

- Static API key in repository secrets: rejected because it is long-lived and insufficiently incident-scoped.
- Existing trusted-header or engine shared-secret authentication: rejected because those trust a different actor and deployment boundary.

**Primary reference**: [GitHub Actions OpenID Connect reference](https://docs.github.com/en/actions/reference/security/oidc)

## Decision 10: Verified webhooks are commands, never state authority

**Decision**: Verify the raw payload using `X-Hub-Signature-256` HMAC-SHA256 and constant-time comparison, reject duplicate delivery IDs, validate installation/repository/event allowlists, then translate supported events into idempotent provider observations or human-command requests.

**Rationale**: Authenticity and replay protection are required before examining issue comments, labels, pull requests, or checks. Even verified content remains untrusted input and cannot directly mutate canonical state.

**Alternatives considered**:

- Poll GitHub only: rejected because it increases latency and does not remove authenticity/idempotency requirements.
- Treat issue labels as state: rejected because users and automation can edit them independently.

**Primary reference**: [Validating GitHub webhook deliveries](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)

## Decision 11: Repair inference and source publication are separate adapters

**Decision**: `IRepairAgentGateway` accepts a bounded request and returns an inert patch/result envelope. `IRepairPatchPublisher` independently parses and validates unified diff content, paths, base SHA, size, forbidden categories, evidence, policy version, kill switches, and attempt lease before using a provider token.

**Rationale**: Existing Weaver is conversational and reads inference credentials; it is not a bounded repair protocol. Separating trust zones makes prompt injection unable to acquire source write authority.

**Alternatives considered**:

- Let the agent push its own branch: rejected because any compromised repository input could exfiltrate or misuse the token.
- Reuse `IWeaverRuntime`: rejected because its streaming conversation/tool contract cannot express deterministic repair evidence and publisher gates.

## Decision 12: Auto-merge is a complete deny-by-default gate matrix

**Decision**: A pure policy evaluates verified producing revision, reproduced failure, before/after regression, independent verification, current required checks, branch protection, low-risk paths/size, excluded change categories, trusted rollout-stop/rollback policy, repository opt-in, and current kill-switch state. Failed, missing, stale, ambiguous, or unknown gates deny auto-merge and are all persisted.

**Rationale**: GitHub branch protection and required checks remain final provider-side constraints, but Valence Control must prove its stricter product policy first.

**Alternatives considered**:

- Auto-merge whenever CI is green: rejected because CI alone says nothing about reproduction, change sensitivity, evidence, rollback readiness, or policy freshness.
- Allow inferred/unverified fixes to auto-merge: rejected; those always require a human.

**Primary references**:

- [GitHub protected branch REST API](https://docs.github.com/en/rest/branches/branch-protection)
- [GitHub status checks](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/collaborating-on-repositories-with-code-quality-features/about-status-checks)

## Decision 13: Provider operations use a leased outbox

**Decision**: Work-item creation/update, workflow dispatch, patch publication, PR update, and merge request are durable provider operations with idempotency keys, atomic leases, heartbeats, retry scheduling, terminal outcomes, and provider correlation IDs.

**Rationale**: Network retries and multiple Valence Control replicas otherwise create duplicate issues, branches, PRs, or merge requests. Existing deployment leases are the closest reusable pattern.

**Alternatives considered**:

- Call GitHub directly inside incident transitions: rejected because a database commit and provider mutation cannot be atomic.

## Decision 14: Dedicated Healing audit is append-only

**Decision**: Every security, policy, evidence, orchestration, provider, human-command, deployment, and verification decision appends a safe structured `HealingAuditEvent`. Application contracts expose append/query only. Events include actor/provider identity, correlation IDs, policy/input hashes, safe reason codes, and timestamps.

**Rationale**: Existing organization audit enums and deployment history are too narrow. Autonomous source changes require reconstructable decision evidence.

**Alternatives considered**:

- Log-only audit: rejected because log retention, schemas, and tenant queries do not provide authoritative product history.

## Decision 15: Healing observes deployment and requires positive verification

**Decision**: Valence Control-managed deployment completion and external delivery systems both append idempotent deployment observations. A verification worker tracks each affected environment and requires the repaired revision, at least one successful affected-operation execution, completion of the window, and no recurrence. It emits a failure signal but never deploys or rolls back.

**Rationale**: Absence of an exception without relevant traffic is not evidence of repair. Existing `DeployedRevisionId` is useful current state but lacks immutable observation provenance and time.

**Alternatives considered**:

- Close on PR merge: rejected because code may never deploy.
- Close on no recurrence alone: rejected because the affected path may not have executed.

## Decision 16: Security hardening is part of the feature, not follow-up

**Decision**: Remove unconditional `IdentityModelEventSource.ShowPII = true`; add explicit Healing permissions; use a platform secret manager for GitHub App credentials; cap bodies, evidence, patches, attempts, time, concurrency, and inference usage; and enforce application/workspace/platform kill switches on every mutation gate.

**Rationale**: Current unconditional identity PII logging conflicts directly with the evidence and credential safety requirements. The feature cannot safely launch with this deferred.

**Alternatives considered**:

- Document the risk for later: rejected because identity claims and provider tokens become part of the feature's normal operation.
