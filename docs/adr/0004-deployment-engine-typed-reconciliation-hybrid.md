# ADR-0004: Fate of Deployment.Engine — Typed Reconciliation Core, Remote Apply

Date: 2026-07-04

## Status

Accepted

## Context

The repository contains two parallel deployment "worlds" that both claim to model
desired-state reconciliation, and they do not meet anywhere in production.

**World 1 — `src/ElsaControl.Deployment.Engine` (the typed reconciler).**
A clean, host-agnostic reconciler: `IDeploymentEngine` with a
`Validate → Read → Diff → DryRun → Apply` pipeline, a per-resource-type
`IResourceHandler` extension point, an ordered `ResourceHandlerRegistry`, typed
`DeploymentResource` / `DeploymentPlan` / `DeploymentChange` / `DeploymentDiagnostic`
models, structured diagnostic codes (`DeploymentEngineDiagnosticCodes`), and an
`IDeploymentHistoryStore`. It is genuinely open/closed and well tested (27 tests).
It is also entirely stranded:

- The `ElsaControl.Deployment.Engine` project is referenced **only** by
  `tests/ElsaControl.Deployment.Engine.Tests`.
- `IResourceHandler` and `IDeploymentTarget` have **zero** production
  implementations.
- `IResourceValidator` and `IResourceStateReader`
  (`src/ElsaControl.Deployment.Abstractions/Resources/`) have **zero**
  implementations anywhere — they are dead abstractions the engine itself does
  not consume (the engine folds validate/read into `IResourceHandler`).

**World 2 — the live path (what actually deploys).**
`DeploymentRunService` → durable run/command records → `DeploymentQueueWorker`
(disabled by default; stale-recovery only) → **remote engines pull, claim, apply,
and report** through the runtime command sync API (spec `028`, `029`, `030`).
The platform is a control plane: it never reads engine state and never applies —
it emits durable deployment commands that a remote runtime applier consumes.
Around that, two hand-rolled services carry the analytical load:

- `DeploymentValidationService` (388 LOC) parses desired-state `records[]` with
  raw `JsonDocument`, diffs source vs. target **by raw payload string equality**
  (`string.Equals(sourceRecord.Payload, targetRecord.Payload, Ordinal)`), and
  hand-builds promotion validations.
- `DeploymentDeployabilityService` (295 LOC) re-parses the same `records[]` shape
  again with its own `JsonDocument` walk, and classifies blockers by
  **substring-matching diagnostic-id strings** (`ScopeFor`: `id.Contains("capability")`,
  `id.Contains("payload")`, `id.Contains("engine")`).

So desired-state JSON is parsed at least twice, by two independent
partial parsers; the "diff" is textual rather than semantic; and blocker scope is
inferred from string spelling rather than typed classification.

**Two parallel model families describing one domain.**
`Cockpit/DeploymentCockpitModels.cs` (string-ID read models:
`WorkflowApplication`, `EnvironmentSummary`, `WorkflowEngineRegistration`,
`DesiredStateRevision`) and `Workspace/WorkspaceDeploymentModels.cs` (Guid-ID
persistence models: `WorkspaceDeploymentApplication`,
`WorkspaceDeploymentEnvironment`, `WorkspaceWorkflowEngine`,
`WorkspaceDesiredStateRevision`) describe the same entities in two shapes, with
manual translation between them.

**A stub in the live path.**
`DeploymentValidationService.PreviewPromotion(request)` (the synchronous overload,
distinct from `PreviewPromotionAsync`) returns a hardcoded
`deployment.preview.not-implemented` blocker. It is still reachable from
`WorkspaceDeploymentEndpoints`.

**Imminent forcing function — spec `037-configuration-shapes`.**
Spec 037 adds a new `ConfigurationBinding` `DesiredStateRecordKind` carried
*inside* desired-state revisions, and requires blocking validation at revision
creation, promotion, and deploy gates (missing required value, type mismatch,
unresolvable secret reference), plus an advisory conformance status between
gates. Every one of those gates is another consumer that will need to parse the
same `records[]`, diff it, and classify diagnostics. Adding a third and fourth
hand-rolled `JsonDocument` walk and a third string-matched blocker classifier is
the path of least resistance and the wrong one. Critically, 037 keeps **delivery
on the existing engine sync channel** and **secret resolution engine-side** —
i.e. it reaffirms the remote-apply architecture, not an in-process one.

### The architectural question that must drive the decision

The engine's pipeline has two halves with very different fit:

- **Analytical half — `Validate` and `Diff`.** Pure functions over *desired
  state* plus *reported target facts* (engine capabilities, heartbeat, last
  reported state). No I/O against a live target is required to compute them. This
  half matches the live path's needs exactly — it is precisely what
  `DeploymentValidationService` and `DeploymentDeployabilityService` already do,
  badly, by hand.
- **Executional half — `Read` and `Apply` (`IResourceHandler.ReadAsync` /
  `ApplyAsync`, `IDeploymentTarget`).** These presume the platform reads current
  state *from* a target and applies changes *to* it, in-process. That is exactly
  what the platform does **not** do. Apply happens **by the remote engine** after
  it pulls a command; the platform only records intent and consumes reported
  results. `IDeploymentTarget` as "a destination the platform reads from and
  applies to" is a category error for this control plane.

This is the decisive point. A naive "integrate the whole engine behind the live
path" (option A) would force us to invent an `IDeploymentTarget` that lies —
wrapping the remote command channel behind a synchronous read/apply façade the
platform can't honor — or to leave `Apply` permanently unimplemented while
pretending the pipeline is whole. A naive "delete it and harden the strings"
(option B) throws away the one genuinely good typed model in the codebase right
before spec 037 multiplies the number of consumers that need it.

## Decision

**Adopt the hybrid (option C): promote the engine's typed *model* and its
*analytical* half (Validate + Diff) into the live path as the single
reconciliation core; keep execution remote.**

Concretely:

1. **Parse desired state ONCE into typed `DeploymentResource` records.** Introduce
   one desired-state reader that turns a revision's `records[]` into the typed
   resource model (reusing `DeploymentResourceId`, `DeploymentResource`,
   `ArtifactDigest`). `DeploymentValidationService`, `DeploymentDeployabilityService`,
   promotion, and the future 037 gates all consume that typed model instead of
   re-walking JSON.

2. **Diff semantically, not textually.** Replace payload-string equality with a
   typed, per-resource-kind diff built on `DeploymentChange` /
   `DeploymentChangeAction`, so "changed" reflects meaningful field-level change,
   not JSON whitespace/key-order.

3. **Classify diagnostics by type, not by substring.** Replace `ScopeFor`'s
   `id.Contains(...)` with a typed diagnostic → scope mapping carried on the
   diagnostic model (the engine already has `DeploymentDiagnostic` with a
   dedicated code enum to build on).

4. **Keep `Apply` remote.** Do **not** implement `IResourceHandler.ApplyAsync` /
   `IDeploymentTarget` for the platform. Apply stays with the remote-engine
   command channel. The engine's `Apply`/`DryRun` methods and the
   `IDeploymentTarget`, `IResourceStateReader`, `IResourceValidator`
   abstractions that presume in-process execution are **not** part of the
   integrated surface.

5. **Delete the dead in-process-execution abstractions.** Remove
   `IResourceValidator` and `IResourceStateReader` (zero implementations, not
   consumed by the engine). Narrow `IResourceHandler` to the analytical surface
   the platform actually uses (`Validate`, `Diff`, and a *pure* current-state
   input rather than a `ReadAsync` that hits a live target), or split it so the
   Read/Apply methods live behind a clearly-labelled "in-process target adapter"
   extension that the platform does not implement.

6. **`ConfigurationBinding` (spec 037) is the first production `IResourceHandler`.**
   The 037 configuration-shape validation (required-value, type, secret-reference
   resolvability, conformance) is implemented as a typed resource handler over the
   shared model — proving the extension point with real production code and
   giving 037 a home that does not add a third JSON walk.

The reconciliation core becomes an in-repo library the live services depend on;
it is **not** re-exposed as `IDeploymentEngine.ApplyAsync`. The name "engine"
is retained for the typed model + validate/diff core only.

### Options considered

- **(A) Integrate the whole engine behind the live path** (typed model *and*
  `Read`/`Apply`/`IDeploymentTarget`). Rejected: the executional half
  fundamentally mismatches the remote-engine pull architecture. It would require
  an `IDeploymentTarget` façade that cannot honor its own contract, or a
  permanently-stubbed `Apply`. It also over-scopes the migration with no payoff,
  since the platform will never apply in-process.

- **(B) Quarantine/delete the engine, harden the strings.** Delete
  `Deployment.Engine` and its abstractions, keep the two hand-rolled services,
  and just make the string diff/classification more careful. Rejected: it
  discards the only well-modelled, well-tested typed domain in the subsystem at
  the exact moment spec 037 adds more gates that need it; it entrenches
  double-parsing and string-matched classification as the permanent design; and
  "harden the strings" is unbounded — semantic diff and typed classification are
  the actual fix, which is most of option C anyway.

- **(C) Hybrid — typed model + shared validate/diff, execution stays remote.**
  Chosen. See rationale below.

## Rationale (5 lines)

1. The engine's Validate/Diff half is a pure function of desired state + reported
   facts — a perfect fit for the live path; its Read/Apply half assumes
   in-process apply, which this control plane structurally does not do.
2. So integrate exactly the half that fits (typed model + validate + diff) and
   leave apply where it correctly lives — with the remote engine.
3. Desired state is parsed twice today by two partial JSON walkers; a single
   typed parse removes an entire class of drift and is the precondition for
   everything else.
4. String-equality diff and substring-matched blocker scope are latent bugs;
   typed diff and typed diagnostics fix them and delete the dead
   `IResourceValidator`/`IResourceStateReader` abstractions on the way.
5. Spec 037 is about to add more gates over the same records; landing the typed
   core first turns 037 into "add one `IResourceHandler`" instead of "add a third
   JSON walk and a third string classifier."

## Migration Plan (ordered, each step shippable)

1. **Land the typed desired-state reader (no behavior change).**
   Add a single `records[]` → `IReadOnlyList<DeploymentResource>` reader in
   `ElsaControl.Deployment.Core` reusing the Abstractions model. Cover it with
   tests mirroring the existing `ParseRecords` / `ParseArtifactReferences` cases.
   Nothing consumes it yet.
   *Files:* new reader in `src/ElsaControl.Deployment.Core/Workspace/`;
   `src/ElsaControl.Deployment.Abstractions/Resources/*`.

2. **Route `DeploymentDeployabilityService` through the typed reader.**
   Replace its private `ParseArtifactReferences` `JsonDocument` walk with the
   shared reader; keep outputs identical. Delete the duplicate parser.
   *Files:* `src/ElsaControl.Deployment.Core/Workspace/DeploymentDeployabilityService.cs`.

3. **Route `DeploymentValidationService` through the typed reader** and replace
   payload **string-equality** diff with a typed per-kind diff built on
   `DeploymentChange` / `DeploymentChangeAction`. Snapshot-test old vs. new diff
   output to prove intended semantic differences only.
   *Files:* `src/ElsaControl.Deployment.Core/Workspace/DeploymentValidationService.cs`;
   `src/ElsaControl.Deployment.Abstractions/Plans/*`.

4. **Replace substring blocker classification with typed diagnostics.**
   Carry scope/category on the diagnostic (extend `DeploymentDiagnostic` /
   `DeploymentEngineDiagnosticCodes`) and delete `ScopeFor`'s `Contains` chain.
   *Files:* `DeploymentDeployabilityService.cs`;
   `src/ElsaControl.Deployment.Abstractions/Diagnostics/*`;
   `src/ElsaControl.Deployment.Engine/DeploymentEngineDiagnosticCodes.cs`.

5. **Introduce the analytical core as a referenced library.**
   Have `Deployment.Core` reference the engine's validate/diff core (or lift it
   into `Deployment.Core`), so the live services call one shared
   `Validate`+`Diff` implementation. Repoint the 27 engine tests at the shared
   core. **Do not** wire `ApplyAsync`.
   *Files:* `src/ElsaControl.Deployment.Engine/*.csproj` references;
   `src/ElsaControl.Deployment.Core/*.csproj`;
   `tests/ElsaControl.Deployment.Engine.Tests/*`.

6. **Delete dead in-process-execution abstractions.**
   Remove `IResourceValidator` and `IResourceStateReader`. Narrow/split
   `IResourceHandler` so the platform-facing surface is Validate+Diff over a pure
   current-state input; move any `Read`/`Apply`/`IDeploymentTarget` remnants
   behind a clearly-labelled optional in-process adapter the platform does not
   implement (or delete them if no near-term consumer exists).
   *Files:* `src/ElsaControl.Deployment.Abstractions/Resources/IResourceValidator.cs`,
   `IResourceStateReader.cs`, `IResourceHandler.cs`;
   `src/ElsaControl.Deployment.Abstractions/Targets/*`;
   `src/ElsaControl.Deployment.Engine/DeploymentEngine.cs` (drop/relocate the
   Read/Apply methods from the platform-facing contract).

7. **Implement spec 037 `ConfigurationBinding` as the first production
   `IResourceHandler`.**
   Its required-value / type / secret-reference-resolvability / conformance checks
   run through the shared typed core at the revision-creation, promotion, and
   deploy gates — no new JSON walk.
   *Files:* new handler in `src/ElsaControl.Deployment.Core/`;
   gate call sites in `DeploymentValidationService` / `DeploymentRunService` /
   `WorkspaceDeploymentEndpoints`; `specs/037-configuration-shapes/`.

## Consequences

Positive:

- Desired state is parsed once into a typed model; all gates (validation,
  deployability, promotion, and 037 configuration) share it.
- Semantic diff and typed diagnostics replace string-equality and substring
  classification — two latent-bug sources removed.
- The good, tested typed model survives and gains its first production consumer;
  the reconciler is honestly scoped to what the control plane actually does.
- Spec 037 lands as a typed handler rather than a third hand-rolled parser.
- Dead abstractions (`IResourceValidator`, `IResourceStateReader`) leave the tree.

Tradeoffs / risks:

- **Diff semantics change.** Moving from raw-string to typed diff can change which
  records read as "changed." Mitigate with old-vs-new snapshot tests (step 3) and
  an explicit review of intended differences.
- **Scope discipline required.** The temptation to "finish the engine" by
  implementing `Apply`/`IDeploymentTarget` must be resisted; apply is remote by
  architecture. The ADR draws that line deliberately.
- **Refactor touches the hot path.** Steps 2–4 change live validation/deployability
  code; ship them behind the existing focused test suites and keep each step
  output-identical except where a bug is being fixed on purpose.
- **Naming.** Retaining the word "engine" for a validate/diff core (no apply) can
  mislead; document the scope, or rename to a "reconciliation core" if confusion
  arises.

## Follow-ups (noted here, not implemented by this ADR)

- **Model-family unification (Cockpit vs. Workspace).**
  `Cockpit/DeploymentCockpitModels.cs` (string-ID read models) and
  `Workspace/WorkspaceDeploymentModels.cs` (Guid-ID persistence models) describe
  the same domain twice with manual translation. Once the typed reconciliation
  model is the shared core, converge these two families onto one canonical
  domain model (Guid-keyed) with an explicit read/presentation projection, rather
  than two hand-maintained parallel shapes. Warrants its own ADR/spec.

- **Remove the stub `PreviewPromotion` overload.**
  `DeploymentValidationService.PreviewPromotion(WorkspacePromotionPreviewRequest)`
  returns a hardcoded `deployment.preview.not-implemented` blocker while
  `PreviewPromotionAsync` is the real implementation. Delete the stub overload and
  repoint `WorkspaceDeploymentEndpoints` (and `WorkspacePermissionModels`
  references) at the async path, so no caller can reach a permanently-not-implemented
  branch.
