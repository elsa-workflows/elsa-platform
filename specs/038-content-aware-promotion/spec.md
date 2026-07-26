# Feature Specification: Content-Aware Promotion

**Feature Branch**: `038-content-aware-promotion`

**Created**: 2026-07-04

**Status**: Draft

**Input**: User description: "Valence Control can know things generic CD tools can't — which activities and features a workflow artifact uses, which packages and features an engine runs, and which configuration keys a feature reads via the mined package manifest schemas. Turn that knowledge into product: (1) artifact-content-aware promotion diffs so users see *what* changed inside a workflow artifact instead of only that a digest changed, and (2) capability pre-flight checks that warn or block when an artifact uses an activity type, feature, or package the target environment's engine does not provide. Content inspection must be static only — the platform must never execute artifact payloads. Engines may be offline and their capability data may be stale, so staleness and verification semantics must be explicit. Keep scope tight: no policy engine, no signing, no multi-party approvals."

## Context & Motivation *(informative)*

Valence Control already treats artifact **payloads as opaque by design** (spec 026, FR-007/FR-008; spec 024, FR-003): the catalog stores envelope metadata, digests, safe diagnostics, and payload references, never raw workflow definitions or manifest JSON. Today the promotion preview (`DeploymentValidationService.CompareArtifacts`) and deployability evaluation (`DeploymentDeployabilityService`) compare artifact **identity + content digest + safe metadata + configuration overlays** only. When two artifact revisions differ, a user sees "Changed" with a differing digest — not *which workflows, activities, variables, or inputs* changed, and not *whether the target engine can even run what the artifact uses*.

Two existing platform assets already know more than the diff surfaces:

- **Static inspection output.** Registration/inspection already produces a `WorkspaceArtifactResourceSummary` list (`Type`, `LogicalId`, `Scope`, `Version`, `DesiredStateHash`) plus a `WorkspaceArtifactManifestSummary` — a structured, per-resource digest of the artifact produced **without executing the payload**. This is the natural anchor for content-aware diffs.
- **Mined package manifests.** `PackageManifest.Generator` mines `FeatureRecord` / `FeatureSettingRecord` into the `PackageCatalog` (feature ids, `RequiredCapabilitiesJson`, dependencies, per-setting config keys and `EnvironmentVariable` bindings). Engines advertise `EngineCapability` (`Id`, `Label`, `Boundary`) via the runtime sync heartbeat. Bridging *what the artifact uses* to *what the engine's running packages provide* is what generic CD tools structurally cannot do.

This feature turns those two assets into product surfaces: richer promotion/revision diffs, and capability pre-flight gating. It is deliberately **additive and backward-compatible** — the envelope contract (spec 026) and the opaque-payload guarantee are preserved.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See What Changed Inside a Promoted Artifact (Priority: P1)

A deployment reviewer opens a promotion preview between two environments and, for each changed artifact, sees a human-readable summary of what changed inside it — which workflow definitions were added, removed, or modified, and at a coarse level which activities, variables, or inputs changed — instead of only "digest changed".

**Why this priority**: This is the headline differentiator. Reviewers approve promotions today with no visibility into the change beyond a digest hash. A content-aware diff is the single most valuable thing the platform can surface that generic CD tools cannot, and it is buildable entirely from already-captured static inspection data without touching the opaque-payload guarantee.

**Independent Test**: Register two artifact revisions that differ only in their inner workflow content, run a promotion preview, and confirm the preview reports the specific added/removed/modified workflow resources (and a change indication for their activities/variables/inputs) rather than a bare digest change.

**Acceptance Scenarios**:

1. **Given** a source and target revision reference the same artifact name with different content digests, **When** a reviewer opens the promotion preview, **Then** the changed artifact shows a content diff summary listing workflow resources added, removed, and modified using safe inspection metadata only.
2. **Given** a modified workflow resource, **When** the reviewer expands it, **Then** the summary indicates the categories of change (for example, activities changed, variables/inputs changed) at the granularity the static inspection data supports, without displaying raw payload content.
3. **Given** an artifact whose content diff cannot be computed (inspection never ran, is stale, unavailable, or unsupported), **When** the reviewer opens the preview, **Then** the preview falls back to the existing digest-level comparison and clearly labels the artifact as "content diff unavailable" with the reason.
4. **Given** two revisions with identical artifact content, **When** the preview is computed, **Then** the artifact is reported as `Unchanged` and no content diff noise is shown.

---

### User Story 2 - Pre-Flight Capability Check Before Promotion or Deploy (Priority: P1)

A deployment reviewer promoting or deploying an artifact is warned or blocked when the artifact uses an activity type, feature, or package that the target environment's engine does not provide, with a diagnostic naming the specific missing capability and where it comes from.

**Why this priority**: A content-aware diff tells reviewers what changed; a capability check tells them whether the target can *run* it. Together they are the value proposition. This extends the existing `artifact.capability.missing` deployability diagnostic from coarse apply-capabilities to content-derived capability requirements, reusing the established capability-normalization and engine-advertisement plumbing.

**Independent Test**: Register an artifact whose inspection metadata indicates use of a feature/activity mapped to a capability the target engine does not advertise, then evaluate promotion/deployability and confirm a capability diagnostic naming the missing capability is produced at the configured severity for the target tier.

**Acceptance Scenarios**:

1. **Given** an artifact that requires a capability the target engine advertises, **When** pre-flight runs, **Then** the artifact passes the capability check with a `Pass` diagnostic.
2. **Given** an artifact that requires a capability the target engine does not advertise, **When** pre-flight runs against a non-production tier, **Then** a `Warning` diagnostic names the missing capability and its source (activity type / feature / package).
3. **Given** the same missing-capability condition against a production-like tier, **When** pre-flight runs, **Then** the diagnostic is raised at `Blocker` severity per the tier's configured gate.
4. **Given** the target engine has reported no capabilities or its capability data is stale, **When** pre-flight runs, **Then** the capability check reports an explicit "cannot verify" outcome (not a false pass and not a hard content-block) and surfaces the staleness reason.
5. **Given** an artifact whose content inspection is unavailable, **When** pre-flight runs, **Then** the platform still performs the existing coarse apply-capability check and marks content-derived capability checks as "not evaluated".

---

### User Story 3 - Content-Aware Revision Comparison (Priority: P2)

An operator comparing two revisions of the same environment (not a cross-environment promotion) sees the same content-aware artifact diff summaries, so the differentiator is available wherever revisions are compared, not only in the promotion flow.

**Why this priority**: The diff computation is the reusable asset; exposing it in revision comparison as well as promotion multiplies its value at low marginal cost. It is P2 because promotion (US1) is the primary decision point.

**Independent Test**: Compare two revisions of one environment that reference differing artifact content and confirm the same content diff summary appears as in the promotion preview.

**Acceptance Scenarios**:

1. **Given** two revisions of one environment with differing artifact content, **When** an operator compares them, **Then** the content diff summary appears using the same model and fallback behavior as the promotion preview.
2. **Given** a revision comparison where one side has no artifact of a given name, **When** the comparison is computed, **Then** the artifact is reported as `Added` or `Removed` consistently with promotion semantics.

---

### User Story 4 - Understand Where Capability Requirements Come From (Priority: P3)

A reviewer investigating a capability warning can see the derivation: which activity type or feature in the artifact required the capability, and how the platform mapped it — so the warning is actionable rather than an opaque id.

**Why this priority**: Actionability. A raw "requires capability X" is a smaller improvement than "workflow *Order Fulfilment* uses feature *Elsa.Http* which requires capability X, not provided by engine *prod-eu-1*". It is P3 because the core gate (US2) delivers value before this explanatory layer.

**Independent Test**: Trigger a capability warning and confirm the diagnostic (or an expandable detail) names the originating artifact resource and the feature/capability mapping used.

**Acceptance Scenarios**:

1. **Given** a missing-capability diagnostic, **When** the reviewer inspects it, **Then** it names the originating workflow resource and the feature/activity-to-capability mapping that produced the requirement, where inspection data supports the attribution.
2. **Given** the mapping is derived from mined package manifest data that is absent or ambiguous, **When** the diagnostic is shown, **Then** it degrades to the capability id alone and labels the attribution as unavailable rather than guessing.

### Edge Cases

- Artifact content inspection has never run, is stale, unavailable, or unsupported — content diff and content-derived capability checks must degrade gracefully to digest-level comparison and the existing coarse apply-capability check.
- The target engine is offline or its heartbeat is stale (existing 15-minute staleness threshold): capability data may be out of date; the check must report an explicit staleness/verification state rather than silently passing or blocking on stale data.
- The target engine has advertised zero capabilities (existing `engine.capabilities.missing`): content-derived capability checks cannot be evaluated and must say so.
- The canonical artifact type is in flux — Studio currently emits `elsa.loom.recipe` while the applier accepts `elsa.workflow-definition` (reconciliation is in flight in a separate effort). Content inspection and capability derivation MUST key off whichever artifact type the record actually carries and MUST NOT hard-code a single canonical id.
- A producer supplies its own content diff summary in display metadata, but it disagrees with platform static inspection — the spec must decide precedence and disclose the source.
- Two artifacts share a name across revisions but are of different artifact types — the diff must treat this as a type change, not a within-type content diff.
- The mined package manifest for a feature the artifact uses is not present in the catalog (unmapped feature) — the capability requirement cannot be derived and must be reported as "unmapped", never assumed satisfied.
- Content inspection metadata is large (many workflows/activities) — the diff summary must remain bounded and safe to render without leaking raw payload.
- An artifact uses an activity type that maps to no known capability at all — treated as informational, not a blocker.
- Legacy revisions created before content-aware inspection existed — must remain viewable with digest-level fallback.

## Requirements *(mandatory)*

### Functional Requirements

**Content-aware diff**

- **FR-001**: System MUST compute an artifact **content diff summary** between a source and target artifact of the same name and artifact type using only safe, statically captured inspection metadata (for example resource summaries with type, logical id, scope, version, and per-resource desired-state hash) and MUST NOT read, download, or execute raw artifact payloads to do so.
- **FR-002**: System MUST report, per changed artifact, which contained resources (for example workflow definitions) were added, removed, or modified, identified by their safe logical identifiers.
- **FR-003**: System MUST indicate, for a modified resource, the categories of change it can determine from static inspection metadata (for example activities changed, variables/inputs changed) at the granularity the inspection data supports, without exposing raw payload content.
- **FR-004**: System MUST preserve the existing digest-level artifact comparison and, when a content diff cannot be computed, MUST fall back to it and label the artifact with a machine-readable reason (never inspected, stale, unavailable, unsupported, or type-changed).
- **FR-005**: System MUST expose the content diff summary in the promotion preview and in revision comparison using a single shared model and identical fallback behavior.
- **FR-006**: System MUST report artifacts with byte-identical content as `Unchanged` and MUST NOT emit content diff detail for them.
- **FR-007**: System MUST keep raw workflow definitions, activity payloads, expressions, variable values, credentials, tokens, and secret values out of the content diff summary, its persistence, its API responses, logs, and history — consistent with the opaque-payload guarantee of specs 024 and 026.

**Source of content knowledge**

- **FR-008**: System MUST define platform-side **static inspection** as the authoritative source of content diff and content-derived capability requirements, extending the existing artifact inspection output rather than introducing a new payload-reading path.
- **FR-009**: System MAY accept a **producer-supplied content summary** carried in the artifact envelope's safe display metadata (spec 026) as supplementary display information, and when both platform inspection and a producer summary are present MUST treat platform inspection as authoritative for gating decisions and MUST disclose which source produced each displayed summary.
- **FR-010**: System MUST keep the artifact envelope contract backward-compatible: content-aware features MUST work for existing artifacts and MUST NOT require any new required envelope field; absence of content data yields graceful fallback, not an error.

**Capability pre-flight**

- **FR-011**: System MUST derive an artifact's **required capabilities** from (a) explicit envelope compatibility hints and artifact-type default required capabilities (existing behavior) and (b) content-derived requirements obtained by mapping activity types / features observed in static inspection metadata to capabilities via the mined package manifest catalog (`FeatureRecord` required capabilities), and MUST union these sources.
- **FR-012**: System MUST compare the artifact's required capabilities against the target engine's advertised capabilities (`EngineCapability` ids), applying the existing capability normalization so legacy and modern capability id forms compare equal.
- **FR-013**: System MUST emit a capability diagnostic for each required capability the target engine does not advertise, naming the missing capability id.
- **FR-014**: System MUST determine capability-diagnostic severity (`Warning` vs `Blocker`) from the target environment tier's configured gate policy, defaulting content-derived capability shortfalls to `Warning` on non-production tiers and `Blocker` on production-like tiers, while preserving existing apply-capability blocking behavior.
- **FR-015**: System MUST, where inspection data supports it, attribute a derived capability requirement to its originating artifact resource and the feature/activity-to-capability mapping that produced it, and MUST degrade to the capability id alone when attribution data is absent or ambiguous rather than guessing.
- **FR-016**: System MUST treat a feature or activity type that maps to no known capability in the catalog as informational (unmapped), never as a satisfied or failed capability.
- **FR-017**: System MUST continue to perform the existing coarse apply-capability check even when content-derived capability derivation is unavailable, and MUST mark content-derived checks as "not evaluated" in that case.

**Staleness & verification semantics**

- **FR-018**: System MUST treat engine capability advertisement as potentially stale and MUST reuse the existing engine capability staleness signals (missing capabilities, heartbeat older than the platform staleness threshold) to qualify capability check outcomes.
- **FR-019**: System MUST, when engine capability data is missing or stale, report capability checks as an explicit "cannot verify" outcome carrying the reason, and MUST NOT record a stale-data result as a confident pass.
- **FR-020**: System MUST, when content inspection metadata for an artifact is stale relative to its current content digest, treat content diff and content-derived capability requirements as unavailable for that artifact and surface the staleness reason.
- **FR-021**: System MUST expose the freshness basis of every content-aware result (the inspection timestamp / digest it was computed from and the engine heartbeat time it was checked against) so reviewers can judge confidence.

**Type-agnosticism & scope guards**

- **FR-022**: System MUST key content inspection, diff, and capability derivation off the artifact type actually recorded on each artifact and MUST remain correct regardless of whether `elsa.workflow-definition` or `elsa.loom.recipe` becomes the canonical workflow artifact type; it MUST NOT hard-code a single canonical id. (See Dependencies.)
- **FR-023**: System MUST NOT introduce a policy engine, artifact signing, or multi-party approval workflow; capability outcomes are advisory-or-gating diagnostics evaluated against existing tier policy only.
- **FR-024**: System MUST enforce existing workspace ownership and read authorization for all content-aware surfaces; content diffs and capability results are workspace-scoped and visible only to authorized members.

### Key Entities *(include if feature involves data)*

- **Artifact Content Diff Summary**: A safe, per-artifact comparison of two revisions of the same-named artifact, derived from static inspection metadata. Lists contained resources added/removed/modified by logical id, per-resource change categories, an overall impact aligned with existing artifact-impact semantics (`Added`, `Changed`, `Removed`, `Unchanged`), and a fallback reason when content-level comparison is unavailable.
- **Content Resource Change**: A single contained resource's change within a diff summary — its logical id, resource type, prior/next version or scope, and the change categories determined from static inspection (for example activities changed, variables/inputs changed). Carries no raw payload.
- **Content Summary Source**: The provenance of a displayed summary — platform static inspection (authoritative for gating) or producer-supplied envelope metadata (supplementary display) — with platform inspection taking precedence for decisions.
- **Derived Capability Requirement**: A capability the artifact requires, with its source classification (envelope hint, artifact-type default, or content-derived via package manifest mapping) and, where available, the originating resource and feature/activity mapping used to derive it.
- **Capability Pre-Flight Outcome**: Per artifact and target engine, the set of required capabilities, which are satisfied/missing/unmapped, the resulting diagnostics and severities per tier policy, and a verification state (verified, cannot-verify-stale, cannot-verify-missing, not-evaluated).
- **Content-Aware Freshness Basis**: The provenance/timestamps a content-aware result was computed from — artifact content digest and inspection timestamp on the artifact side, and engine heartbeat/capability freshness on the engine side.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a promotion where an artifact's inner workflow content changed, a reviewer can identify the specific added/removed/modified workflow resources from the preview without opening the artifact payload, in 100% of cases where valid static inspection metadata exists on both sides.
- **SC-002**: 0 occurrences of raw workflow payloads, expressions, variable values, or secret values appearing in content diff summaries, their persistence, API responses, logs, or history during acceptance testing.
- **SC-003**: When an artifact requires a capability the target engine does not advertise, a capability diagnostic naming the missing capability is produced in 100% of evaluations, at severity matching the target tier's configured gate.
- **SC-004**: When engine capability data is missing or stale, capability checks report an explicit "cannot verify" outcome (with reason) and never a confident pass, in 100% of stale/missing-data cases.
- **SC-005**: Content-aware surfaces produce correct results for artifacts of either candidate workflow artifact type without code changes tied to a specific canonical id.
- **SC-006**: Artifacts lacking valid content inspection fall back to digest-level comparison and the existing coarse apply-capability check with a labeled reason, with no errors, in 100% of fallback cases.
- **SC-007**: The content diff and capability derivation are exercised identically in both promotion preview and revision comparison, verified by shared-model acceptance tests.

## Assumptions

- Static inspection already captures per-resource summaries (resource type, logical id, scope, version, per-resource desired-state hash) sufficient to detect resource add/remove/modify and coarse within-resource change categories; where current granularity is insufficient for a change category, that category is reported as "changed, detail unavailable" rather than fabricated.
- The mined package manifest catalog (`FeatureRecord` required capabilities and settings) is the mapping source from features/activity types to capabilities; where a feature is absent from the catalog its requirement is reported as unmapped.
- Engine capability advertisement via the runtime sync heartbeat (`EngineCapability`) and the existing heartbeat-staleness threshold are the source of truth for what a target engine can run.
- Tier gate policy (which capability shortfalls warn vs block per environment tier) reuses the existing tier-capability/validation-severity model; this spec adds capability-shortfall gating to it without introducing a new policy mechanism.
- The opaque-payload guarantee (specs 024/026) is inviolable: content awareness is achieved by enriching and exposing static inspection metadata, never by reading payloads at diff or gate time.

## Dependencies

- **Canonical artifact-type reconciliation (in flight, separate effort)**: Studio emits `elsa.loom.recipe` while the runtime applier accepts `elsa.workflow-definition`; reconciliation is being handled elsewhere. This spec is intentionally agnostic to the outcome (FR-022, SC-005) and depends only on the artifact type being consistently recorded per artifact. No decision here should presuppose which id wins.
- **Static inspection depth**: Realizing within-resource change categories (activities/variables/inputs changed) and content-derived capability requirements depends on the inspection stage capturing enough per-resource structure. If current inspection output is resource-level only, planning must decide whether to deepen inspection or scope the first release to resource-level add/remove/modify.

## Out of Scope

- Policy engine, rule authoring, or configurable approval logic beyond existing tier gate severities.
- Artifact signing, provenance attestation, or supply-chain verification.
- Multi-party or staged human approval workflows.
- Executing, sandboxing, or partially evaluating artifact payloads to compute diffs or capabilities.
- Live runtime drift detection or reconciliation of what an engine is *actually* running versus advertised capabilities.
- Auto-remediation of missing capabilities (installing appliers/features on engines) — checks are diagnostic; remediation guidance is textual only.
- OCI, GitOps, or external-provider apply mechanics (already out of scope in specs 024/030).
