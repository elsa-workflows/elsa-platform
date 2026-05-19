# Elsa Deployment Platform Phased Implementation Strategy

Originating PRD: [elsa-workflows/elsa-core#7469](https://github.com/elsa-workflows/elsa-core/issues/7469)

Status: draft implementation strategy

## 1. Product Framing

The Elsa Deployment Platform is the deployment control plane for Elsa-based systems. It packages, validates, previews, applies, and records declarative desired state for runtime configuration and workflow assets across environments.

The first product promise is not "deploy everything". The first product promise is a dependable deployment loop:

```text
manifest -> artifact -> validation -> dry-run -> apply -> history
```

The platform must remain separate from runtime execution. It reconciles deployable control-plane resources such as workflow definitions, variables, feature declarations, packages, and recipes. It must not reconcile workflow instances, bookmarks, execution state, execution logs, distributed locks, runtime queues, or transient runtime state.

The long-term product path is:

1. Phase 1 proves the core deployment loop for a small, useful resource set.
2. Phase 2 adds enterprise operational controls around the same loop.
3. Phase 3 turns the loop into a platform engineering substrate for fleets, tenants, Kubernetes, and distributed reconciliation.

## 2. Architecture Principles

- Control-plane/data-plane separation is non-negotiable.
- The reconciliation engine is host-agnostic and transport-agnostic.
- APIs and CLI commands call the same engine contracts.
- The manifest model is declarative, versioned, and Git-friendly.
- Artifacts are immutable once built.
- Reconciliation is idempotent per resource type.
- Phase 1 abstractions must be narrow, but not throwaway.
- Enterprise and operator features must plug into the same engine rather than create separate deployment paths.
- Resource extensibility must exist early, but advanced resource types can wait.
- Storage, transport, hosting, and packaging choices must be replaceable behind explicit ports.

## 3. Repository And Solution Structure

Recommended initial repository structure:

```text
elsa-platform/
  Elsa.Platform.sln
  docs/
    deployment-platform-phased-strategy.md
    adr/
  src/
    Elsa.Platform.Deployment.Abstractions/
    Elsa.Platform.Deployment.Manifest/
    Elsa.Platform.Deployment.Artifacts/
    Elsa.Platform.Deployment.Engine/
    Elsa.Platform.Deployment.Cli/
    Elsa.Platform.Deployment.Api/
    Elsa.Platform.PackageCatalog.Abstractions/
    Elsa.Platform.PackageCatalog.Core/
    Elsa.Platform.PackageCatalog.Api/
    Elsa.Platform.PackageCatalog.AdminUi/
    Elsa.Platform.PackageCatalog.Sources.NuGet/
    Elsa.Platform.PackageManifests/
    Elsa.Platform.PackageManifest.Generator/
  tests/
    Elsa.Platform.Deployment.Manifest.Tests/
    Elsa.Platform.Deployment.Artifacts.Tests/
    Elsa.Platform.Deployment.Engine.Tests/
    Elsa.Platform.Deployment.Cli.Tests/
    Elsa.Platform.Deployment.Api.Tests/
    Elsa.Platform.PackageCatalog.*.Tests/
    Elsa.Platform.PackageManifests.Tests/
    Elsa.Platform.PackageManifest.Generator.*.Tests/
  samples/
    basic-workflow-deployment/
    ci-dry-run/
```

Before implementation scaffolding begins, initialize Spec Kit in this repository with the `specify init` command and keep the resulting spec workflow aligned with this strategy document. The planning document is the roadmap; Spec Kit should become the execution workspace for concrete feature specifications once Phase 1 build work starts.

Deferred structure:

```text
src/
  Elsa.Platform.Deployment.GitOps/
  Elsa.Platform.Deployment.Operator/
  Elsa.Platform.Deployment.Kubernetes/
  Elsa.Platform.Deployment.Oci/
  Elsa.Platform.Deployment.Signing/
  Elsa.Platform.Deployment.Policies/
```

### Elsa Core Versus Elsa Platform Ownership

`elsa-core` should own only contracts and runtime integration points that must be versioned with the runtime:

- Stable runtime APIs needed to import, activate, validate, and inspect workflow definitions.
- Runtime feature/package capability discovery contracts.
- Compatibility hooks for deployment validation.
- Minimal resource handlers only when they must live beside runtime internals.

`elsa-platform` should own platform behavior:

- Manifest schema and parsing.
- Artifact creation, inspection, and immutability rules.
- Deployment planning, validation orchestration, diffing, dry-run, apply, and history.
- Package catalog, package manifest contracts, package manifest generation, package approval, and package compatibility metadata.
- CLI, API, operator, GitOps, OCI, signing, approvals, overlays, governance, and platform documentation.
- Resource handler abstractions and default handlers that can operate through public Elsa runtime APIs.

Package Catalog is a sibling platform subsystem, not a child of Deployment. Deployment should consume package catalog capabilities through abstractions or client contracts for package descriptor validation, approval state, and compatibility checks.

## 4. Package And Module Boundaries

### Elsa.Platform.Deployment.Abstractions

Stable contracts shared by all platform packages:

- `IDeploymentResource`
- `DeploymentResourceId`
- `DeploymentResourceType`
- `DeploymentManifest`
- `DeploymentArtifact`
- `DeploymentPlan`
- `DeploymentChange`
- `DeploymentResult`
- `DeploymentStatus`
- `IResourceHandler`
- `IResourceStateReader`
- `IResourceValidator`
- `IArtifactReader`
- `IArtifactWriter`
- `IDeploymentHistoryStore`
- `IDeploymentEngine`
- `IDeploymentTarget`

Keep this package intentionally small. It should define the deployment language, not implementation details.

### Elsa.Platform.Deployment.Manifest

Manifest parsing, schema validation, normalization, and versioning:

- YAML and JSON manifest readers.
- Schema version mapping.
- Resource normalization into typed deployment resources.
- Manifest diagnostics.
- Future overlay hooks, but no overlay implementation in Phase 1 unless forced by implementation feedback.

### Elsa.Platform.Deployment.Artifacts

Artifact creation and reading:

- Phase 1 folder and ZIP artifacts.
- Artifact metadata and checksums.
- Immutable artifact identity.
- Artifact inspection APIs.
- Future adapters for OCI and NuGet.

### Elsa.Platform.Deployment.Engine

The host-agnostic reconciliation engine:

- Plan construction.
- Validation pipeline.
- Diff calculation.
- Dry-run execution.
- Apply orchestration.
- Resource handler dispatch.
- Deployment history recording.
- Failure and resume model.

The engine must not depend on ASP.NET hosting, a specific database, Kubernetes, GitHub Actions, or a particular CLI framework.

### Elsa.Platform.Deployment.Cli

CI/CD and local automation surface:

- `elsa deploy build`
- `elsa deploy validate`
- `elsa deploy diff`
- `elsa deploy dry-run`
- `elsa deploy apply`
- `elsa deploy history`
- `elsa deploy inspect`

The CLI should be the first complete user-facing implementation because it proves portability and CI/CD fit quickly.

### Elsa.Platform.Deployment.Api

HTTP API and service registration for hosted scenarios:

- Deployment execution endpoint.
- Validation endpoint.
- Diff and dry-run endpoints.
- Deployment history endpoints.
- Artifact inspection endpoint.

Phase 1 can expose a minimal API after the engine is stable enough. API shape should mirror engine operations and avoid extra workflow-specific behavior.

## 5. Phase Roadmap Summary

| Capability | Phase 1: Foundation | Phase 2: Enterprise | Phase 3: Platform Engineering |
| --- | --- | --- | --- |
| Manifest schema | Versioned v1alpha, single manifest | Stable v1, overlays | Multi-tenant and fleet layering |
| Artifact format | Folder + ZIP | OCI-compatible, signed | Attested supply-chain artifacts |
| Resource types | Workflows, variables, feature declarations; package/recipe descriptors if practical | Secret references, approvals, promotion resources, drift policies | Tenant, fleet, rollout, policy resources |
| Reconciliation host | CLI-first, engine library, optional minimal API | External operator and GitOps agent | Distributed reconcilers |
| Validation | Schema, resource, compatibility checks | Policy-aware and signature-aware validation | Organization-wide policy engine |
| Dry-run and diff | Required | Drift-aware and promotion-aware | Fleet and tenant diff |
| Apply | Idempotent per resource | Approval-gated and resumable | Progressive and distributed |
| History | Local/runtime-backed deployment history | Auditable enterprise history | Fleet-wide audit and attestations |
| Governance | Deployment identity and actor metadata | Approvals, signing, promotion gates | Policy enforcement and attestations |
| Kubernetes | Non-goal | Optional operator integration only | CRDs and native reconciliation |
| Multi-tenancy | Non-goal except naming discipline | Tenant-aware planning experiments | First-class tenant reconciliation |

## 6. Phase 1 MVP Scope

Phase 1 proves the deployment loop with the smallest useful resource model.

Required capabilities:

- Parse a versioned environment manifest.
- Build an immutable deployment artifact from a folder.
- Read an artifact from a folder or ZIP.
- Validate manifest structure and resource references.
- Produce a deterministic deployment plan.
- Produce a dry-run preview.
- Apply resources idempotently.
- Record deployment history.
- Represent partial failure and allow safe resume by re-applying the same artifact.
- Provide CLI commands for build, validate, diff, dry-run, apply, inspect, and history.
- Provide resource handler extension points.

### Phase 1 Resource Slice

The Phase 1 implementation should fully reconcile:

- Workflow definitions.
- Variables.

The Phase 1 implementation should support as descriptors and validation inputs, with apply support only where existing stable runtime APIs make it cheap:

- Feature declarations.
- Package requirements.
- Recipe references.

This narrower slice is intentional. Workflows and variables prove resource identity, desired state, diff, apply, history, rollback semantics, and idempotency without forcing premature package management, feature activation, or Loom integration decisions.

### Phase 1 Non-Goals

- Kubernetes CRDs.
- OCI artifacts.
- Artifact signatures.
- Policy engines.
- Multi-tenant reconciliation.
- Distributed operators.
- Advanced overlays.
- Secret value management.
- Runtime state reconciliation.
- Automatic distributed rollback.
- Workflow instance migration.
- GitOps controllers.
- Approval workflows.

## 7. Phase 2 Enterprise Scope

Phase 2 adds operational maturity while preserving the Phase 1 engine.

Candidate capabilities:

- Drift detection.
- Advisory and strict drift modes.
- Approval gates.
- Signed artifacts.
- OCI artifact compatibility.
- External operator service.
- GitOps integration.
- Secret references.
- Environment overlays.
- Promotion flows between environments.
- Better rollback guidance.
- Deployment locks.
- Rich audit metadata.
- Deployment retention and pruning.
- Web/API integration suitable for enterprise portals.

### Phase 2 Non-Goals

- Kubernetes CRDs as the primary resource model.
- Fleet-wide distributed reconciliation.
- Progressive rollout orchestration.
- Full policy engine.
- Cross-tenant reconciliation semantics.
- Reconciliation of workflow instances or runtime state.

## 8. Phase 3 Platform Engineering Scope

Phase 3 turns the deployment platform into a large-scale platform engineering capability.

Candidate capabilities:

- Multi-tenant reconciliation.
- Fleet management.
- Kubernetes CRDs.
- Progressive rollout.
- Policy engine.
- Attestations.
- Distributed reconciliation.
- Tenant and environment inheritance.
- Platform-wide deployment dashboards.
- Resource health aggregation.
- Cross-environment convergence reporting.

### Phase 3 Non-Goals

- Owning Elsa workflow execution semantics.
- Mutating workflow instance state as part of deployment.
- Replacing infrastructure provisioning tools.
- Becoming Kubernetes-only.

## 9. Decision Gates Between Phases

### Gate From Phase 1 To Phase 2

Advance only after:

- Workflow and variable resources are idempotent under repeated apply.
- Deployment history is useful enough to diagnose success, no-op, change, and failure.
- Dry-run output is trusted by tests and by at least one realistic sample.
- Artifact identity and checksum behavior are stable.
- Resource handler extension points have implemented at least two resource types.
- Partial failure and resume behavior is documented and tested.
- The CLI can run in CI without requiring a hosted Elsa management server beyond the target runtime API.

Decisions to make at this gate:

- Whether drift is advisory, strict, or resource-specific by default.
- Whether the API package should become mandatory or remain optional.
- Which artifact metadata fields are stable for signing.
- Whether package and recipe apply behavior belongs in Phase 2 or a Phase 1.x increment.

### Gate From Phase 2 To Phase 3

Advance only after:

- External operator and CLI produce the same deployment plans for the same inputs.
- Signed artifact verification is stable.
- Promotion flow semantics are proven in at least two environments.
- Secret references are supported without storing raw secret values.
- Overlay behavior is stable enough to model tenants and fleets.
- Drift detection is resource-specific and operationally useful.

Decisions to make at this gate:

- Whether Kubernetes CRDs should mirror manifests directly or compile into manifests.
- Whether policy evaluation is embedded, external, or both.
- Which reconciliation responsibilities can be distributed safely.
- Which tenant resource scopes are global, environment-scoped, or tenant-scoped.

## 10. Technical Risks And Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Resource taxonomy becomes too broad too early | Slow Phase 1 and unstable contracts | Fully implement workflows and variables first; use descriptors for packages, features, and recipes until APIs prove stable |
| Platform leaks into runtime state | Unsafe reconciliation and data loss | Hard-code non-goals into docs and tests; avoid handlers for instances, bookmarks, logs, locks, queues, and execution state |
| Artifact format churn | Broken CI/CD and signing path | Define artifact metadata and identity before adding OCI or signatures |
| Idempotency differs by resource | Inconsistent apply behavior | Require each handler to document identity, desired state hash, diff, apply, no-op, delete policy, and conflict behavior |
| API shape diverges from CLI | Duplicate products | Make CLI and API call the same engine contracts |
| History model too weak | Poor auditability | Record deployment id, artifact identity, manifest identity, actor, target, plan, changes, statuses, diagnostics, and timestamps from Phase 1 |
| Partial failures are ambiguous | Unsafe retries | Persist per-resource operation results and define reapply as the Phase 1 resume mechanism |
| Overlay design is premature | Complex unstable manifest language | Defer overlays until Phase 2, but keep manifest normalization separate from parsing |
| Package/recipe integration is unclear | Phase 1 scope creep | Treat package and recipe resources as descriptors first; implement apply only behind explicit handlers |

## 11. First Deployable Resource Taxonomy

Every deployable resource should have:

- Type.
- Stable logical id.
- Optional version.
- Desired state payload.
- Desired state hash.
- Dependencies.
- Scope.
- Handler name.
- Deletion behavior.

Recommended Phase 1 taxonomy:

| Resource | Phase 1 behavior | Identity | Idempotency rule |
| --- | --- | --- | --- |
| `workflowDefinition` | Full validate, diff, dry-run, apply, history | Stable workflow definition id plus version or version strategy | Same desired definition produces no-op; changed desired definition creates/imports a new version or updates only when explicitly configured |
| `variable` | Full validate, diff, dry-run, apply, history | Variable key plus scope | Same key and value hash produces no-op; changed value updates control-plane variable |
| `feature` | Descriptor and validation; apply if stable runtime feature API exists | Feature id | Desired enabled/disabled state compared to runtime capability state |
| `package` | Descriptor and validation; package installation deferred unless Nuplane API is ready | Package id plus version range | Runtime must satisfy version; install behavior deferred |
| `recipe` | Descriptor and validation; execution deferred unless Loom API is ready | Recipe id plus version/hash | Recipe execution must be explicit and recorded; avoid hidden imperative mutation |

Deletion in Phase 1 should be conservative. The manifest can describe desired resources, but destructive deletion should require explicit per-resource settings and should not be the default.

## 12. Manifest Schema Strategy

Start with `apiVersion: platform.elsa.io/v1alpha1` and `kind: EnvironmentManifest`.

Example shape:

```yaml
apiVersion: platform.elsa.io/v1alpha1
kind: EnvironmentManifest
metadata:
  name: sales-staging
  version: 2026.05.19.1
resources:
  workflows:
    - id: order-approval
      path: workflows/order-approval.json
      activation: active
  variables:
    - key: orderTimeout
      value: 30
  features:
    - id: sales
      state: enabled
  packages:
    - id: Acme.Sales
      version: 1.4.2
  recipes:
    - id: initialize-sales
      path: recipes/initialize-sales.yaml
```

Schema rules:

- Use a single manifest in Phase 1.
- Support YAML first; JSON is acceptable through the same model.
- Normalize manifests into typed resource records before planning.
- Include schema version in every manifest and artifact.
- Keep environment overlays out of Phase 1.
- Preserve unknown extension metadata where safe, but reject unknown resource types unless a handler is registered.
- Keep secrets as references only in Phase 2.

Contracts that should stabilize before implementation starts:

- Resource identity format.
- Manifest schema version format.
- Resource handler registration model.
- Deployment plan and change result model.
- Artifact metadata fields needed for history and future signing.

## 13. Artifact Format Strategy

Phase 1 should support:

- Folder artifacts for development and tests.
- ZIP artifacts for CI/CD portability.

The artifact abstraction should exist from the start:

- `IArtifactReader`.
- `IArtifactWriter`.
- `DeploymentArtifactMetadata`.
- `ArtifactDigest`.
- `ArtifactLayout`.

Phase 1 artifact layout:

```text
artifact/
  manifest.yaml
  artifact.json
  workflows/
  recipes/
  checksums.json
```

Phase 1 artifact identity:

- Artifact id.
- Artifact version.
- Manifest digest.
- Content digest.
- Build timestamp.
- Builder metadata.
- Source commit when available.

Deferred formats:

- NuGet package as distribution wrapper.
- OCI artifact as enterprise/GitOps distribution wrapper.
- Signed artifact envelopes.
- Attestation bundles.

The important early decision is that the engine reads artifacts through an abstraction and never assumes a transport.

## 14. Reconciliation Engine Strategy

The engine should be a deterministic pipeline:

```text
load artifact
-> parse manifest
-> normalize resources
-> read target state
-> validate
-> plan
-> diff
-> dry-run or apply
-> record history
```

Core interfaces:

- `IDeploymentEngine`.
- `IDeploymentPlanner`.
- `IDeploymentValidator`.
- `IDeploymentDiffer`.
- `IResourceHandler`.
- `IDeploymentHistoryStore`.
- `IDeploymentTarget`.

Resource handlers own resource-specific behavior:

- Read current state.
- Validate desired state.
- Compare desired and actual state.
- Produce changes.
- Apply changes.
- Report operation results.

Phase 1 rollback means applying a previous known-good artifact or, for workflows, reactivating a previously deployed version when the workflow handler can prove the prior version exists. It does not mean automatic distributed transaction rollback.

Partial failures should be represented as:

- Deployment status: `Failed`, `PartiallyApplied`, or `CompletedWithWarnings`.
- Per-resource operation status.
- Diagnostic messages.
- Retryability flag.
- Last successful operation.
- Artifact and plan identity.

Resume in Phase 1 should be re-apply of the same artifact. The engine should detect already-converged resources and continue with failed or pending resources.

## 15. CLI Strategy

The CLI is the Phase 1 product surface because it proves CI/CD and local portability without requiring a hosted platform.

Recommended commands:

```text
elsa deploy build --manifest manifest.yaml --output dist/sales.zip
elsa deploy inspect dist/sales.zip
elsa deploy validate dist/sales.zip --target staging
elsa deploy diff dist/sales.zip --target staging
elsa deploy dry-run dist/sales.zip --target staging
elsa deploy apply dist/sales.zip --target staging
elsa deploy history --target staging
```

CLI design rules:

- Output human-readable text by default.
- Support `--output json` for automation.
- Return non-zero exit codes for validation failure, apply failure, and unsafe diff.
- Avoid storing credentials in artifacts.
- Use target profiles for runtime connection details.
- Keep command behavior aligned with API operations.

## 16. API Strategy

Phase 1 can start CLI-first, but the API contracts should be visible early so the engine is not accidentally CLI-shaped.

Minimal API surface:

- `POST /deployment-artifacts/validate`
- `POST /deployments/diff`
- `POST /deployments/dry-run`
- `POST /deployments/apply`
- `GET /deployments/{id}`
- `GET /deployments`
- `GET /deployment-artifacts/{id}`

Phase 1 API can be optional and thin. Phase 2 should harden it for approvals, operators, GitOps, and enterprise portals.

API rules:

- APIs operate on artifacts and targets, not raw runtime state.
- APIs call the same engine as the CLI.
- APIs return deployment plans, changes, diagnostics, and history records using stable DTOs.
- APIs must not expose endpoints for reconciling workflow instances, bookmarks, logs, locks, queues, or transient runtime state.

## 17. Validation, Dry-Run, And Diff Strategy

Validation layers:

1. Manifest schema validation.
2. Artifact integrity validation.
3. Resource reference validation.
4. Target capability validation.
5. Resource-specific validation.
6. Optional policy validation in later phases.

Diff output should distinguish:

- `Create`.
- `Update`.
- `Activate`.
- `Deactivate`.
- `NoOp`.
- `Delete`.
- `Unsupported`.
- `Conflict`.

Dry-run should produce the exact plan apply would execute, without mutating target state. Dry-run reliability is a Phase 1 release gate.

Diff and dry-run should be available as:

- Human-readable CLI output.
- JSON CLI output.
- API DTOs.
- Stored deployment history snapshots.

## 18. Governance And Audit Strategy

Phase 1 governance model:

- Deployment id.
- Deployment revision.
- Artifact id and digest.
- Manifest digest.
- Target id.
- Actor.
- Source commit metadata when available.
- Started and completed timestamps.
- Status.
- Per-resource changes and results.
- Diagnostics.

Phase 2 additions:

- Approval records.
- Signed artifacts.
- Signature verification status.
- Promotion lineage.
- Drift records.
- Deployment locks.
- Secret reference audit events.

Phase 3 additions:

- Attestations.
- Policy decisions.
- Fleet-wide history.
- Tenant-scoped audit views.
- Distributed reconciler identity.

## 19. Testing Strategy

Test layers:

- Manifest schema tests.
- Manifest normalization tests.
- Artifact layout and checksum tests.
- Resource handler contract tests.
- Engine planning tests.
- Dry-run versus apply consistency tests.
- Idempotency tests for every resource type.
- Partial failure and resume tests.
- CLI command tests.
- API endpoint contract tests.
- Golden output tests for CLI JSON.

Test style should stay clean:

- Use instance fields and constructor setup for repeated fixtures.
- Extract activation/deployment helpers when they clarify behavior.
- Use `IAsyncDisposable` for test teardown where targets or temporary resources need cleanup.
- Keep tests focused on deployment behavior rather than runtime internals.

Phase 1 must include at least one end-to-end sample test:

```text
manifest -> ZIP artifact -> validate -> dry-run -> apply -> history -> re-apply no-op
```

## 20. GitHub Issue Breakdown For Phase 1

Recommended labels: `area:deployment`, `phase:1`, `type:feature`, `type:design`, `type:test`, `decision`.

1. Define Phase 1 deployment contracts in `Elsa.Platform.Deployment.Abstractions`
   - Acceptance: resource identity, resource handler, plan, change, result, artifact, target, and history contracts are documented and covered by contract tests.

2. Add solution and initial project structure
   - Acceptance: Spec Kit has been initialized with `specify init`; solution builds with the Phase 1 package skeletons and test projects.

3. Implement v1alpha manifest parser and schema validation
   - Acceptance: YAML manifests parse into normalized resources and invalid manifests return structured diagnostics.

4. Implement deployment artifact folder layout
   - Acceptance: artifact folders include manifest, metadata, resources, and checksums.

5. Implement ZIP artifact reader and writer
   - Acceptance: folder and ZIP artifacts produce equivalent metadata and content digests.

6. Implement deployment planner
   - Acceptance: planner produces deterministic resource operations from desired and actual state.

7. Implement validation pipeline
   - Acceptance: schema, artifact, resource, and target capability diagnostics are aggregated consistently.

8. Implement diff model and renderer
   - Acceptance: CLI and JSON outputs represent create, update, activate, no-op, unsupported, and conflict changes.

9. Implement dry-run execution
   - Acceptance: dry-run uses the same plan as apply and does not mutate target state.

10. Implement deployment history store abstraction and baseline implementation
    - Acceptance: deployment metadata, plan, per-resource results, diagnostics, and timestamps are persisted.

11. Implement workflow definition resource handler
    - Acceptance: workflow resources validate, diff, apply, record history, and no-op on repeated apply.

12. Implement variable resource handler
    - Acceptance: variable resources validate, diff, apply, record history, and no-op on repeated apply.

13. Add package, feature, and recipe descriptor support
    - Acceptance: descriptors participate in manifest validation and target capability checks; apply behavior is either implemented behind stable APIs or explicitly reported as unsupported.

14. Implement CLI build, inspect, and validate commands
    - Acceptance: commands work against folder and ZIP artifacts and support JSON output.

15. Implement CLI diff, dry-run, apply, and history commands
    - Acceptance: commands run the complete deployment loop against a configured target.

16. Add minimal deployment API package
    - Acceptance: API endpoints wrap engine validate, diff, dry-run, apply, and history operations without duplicating engine logic.

17. Add Phase 1 end-to-end sample
    - Acceptance: sample demonstrates manifest to artifact to dry-run to apply to history to no-op reapply.

18. Document rollback, partial failure, and resume semantics
    - Acceptance: docs explain reapply-based resume and previous-artifact rollback; tests cover retry after partial failure.

19. Add ADR for artifact format and identity
    - Acceptance: ADR records folder/ZIP first and OCI/signing deferred.

20. Add ADR for reconciliation hosting model
    - Acceptance: ADR records CLI-first engine library with optional minimal API and deferred operator.

## 21. Backlog Candidates For Later Phases

Phase 2 candidates:

- Drift detection engine.
- Approval workflow integration.
- Signed artifacts.
- OCI artifact reader/writer.
- External operator service.
- GitOps watcher.
- Secret reference resource handler.
- Overlay model.
- Promotion flow tracking.
- Deployment locks.
- Runtime capability inventory API.
- Enterprise audit export.

Phase 3 candidates:

- Kubernetes CRDs.
- Tenant-scoped manifests.
- Fleet reconciliation controller.
- Progressive rollout resources.
- Policy engine.
- Attestation support.
- Distributed reconciler coordination.
- Fleet dashboards.
- Tenant override inheritance.
- Organization-wide governance reporting.

## 22. ADR Candidates And Decision Log

| ADR | Phase | Decision |
| --- | --- | --- |
| ADR-0001 | Phase 1 | Control-plane/data-plane separation and explicit runtime-state non-goals |
| ADR-0002 | Phase 1 | Initial package structure and `elsa-core` versus `elsa-platform` ownership |
| ADR-0003 | Phase 1 | Manifest schema versioning and v1alpha shape |
| ADR-0004 | Phase 1 | Folder and ZIP artifacts before OCI/NuGet/signing |
| ADR-0005 | Phase 1 | CLI-first deployment loop with API-compatible engine contracts |
| ADR-0006 | Phase 1 | Resource handler contract and idempotency requirements |
| ADR-0007 | Phase 1 | Deployment history model and partial failure representation |
| ADR-0008 | Phase 2 | Drift philosophy: advisory, strict, or resource-specific |
| ADR-0009 | Phase 2 | Artifact signing and verification model |
| ADR-0010 | Phase 2 | Overlay model |
| ADR-0011 | Phase 2 | External operator hosting model |
| ADR-0012 | Phase 3 | Kubernetes CRD mapping strategy |
| ADR-0013 | Phase 3 | Multi-tenant resource scope model |
| ADR-0014 | Phase 3 | Policy engine integration model |

Open decisions to defer until implementation feedback exists:

- Whether package installation belongs in Phase 1.x or Phase 2.
- Whether recipes are declarative resources, explicit operations, or both.
- Whether feature activation should be strict reconciliation or advisory validation.
- Whether workflow deployment should always create immutable versions or allow controlled mutable updates for development targets.
- Whether history storage should be target-local, platform-local, or both.
- Whether rollback should grow into compensating operations or remain previous-artifact reapply plus resource-specific restoration.

## 23. Future Plug-In Strategy

GitOps, operators, APIs, and third-party resource packages should plug into the same engine.

Required extension points:

- Resource handler registration.
- Manifest resource type binding.
- Artifact reader/writer registration.
- Target connector registration.
- Validation rule registration.
- History store provider.
- Diff renderer.
- Credentials provider.

Extension packages should be able to add new deployable resource types without changing the core engine, provided they implement the resource handler contract and declare schema requirements.

## 24. Short Comment For The PRD Issue

Suggested comment for [elsa-workflows/elsa-core#7469](https://github.com/elsa-workflows/elsa-core/issues/7469):

```markdown
I converted the PRD into a phased implementation strategy in the new `elsa-platform` repository:

<link to docs/deployment-platform-phased-strategy.md>

Summary of the proposed approach:

- Phase 1 proves the core loop: manifest -> artifact -> validation -> dry-run -> apply -> history.
- The first fully reconciled resources should be workflow definitions and variables. Packages, features, and recipes should start as descriptors/validation inputs unless stable runtime APIs make apply support cheap.
- Folder and ZIP artifacts come first; OCI, signing, approvals, overlays, GitOps, and external operators are deferred to Phase 2.
- Phase 3 is reserved for platform engineering capabilities such as multi-tenant reconciliation, fleet management, Kubernetes CRDs, progressive rollout, policy engines, attestations, and distributed reconciliation.
- The strategy preserves strict control-plane/data-plane separation and explicitly excludes workflow instances, bookmarks, execution state, logs, locks, queues, and transient runtime state from reconciliation.

The document also includes the proposed solution/project structure, capability matrix, ADR candidates, Phase 1 GitHub issue breakdown, and later-phase backlog candidates.
```
