# Contract: Deployment Engine MVP

## Public Package

`ValenceControl.Deployment.Engine`

## Primary Service

`DeploymentEngine` implements `IDeploymentEngine`.

Operations:

- `ValidateAsync(IArtifactReader artifact, IDeploymentTarget target, DeploymentExecutionContext? context = null, CancellationToken cancellationToken = default)`
- `DiffAsync(IArtifactReader artifact, IDeploymentTarget target, DeploymentExecutionContext? context = null, CancellationToken cancellationToken = default)`
- `DryRunAsync(DeploymentPlan plan, IDeploymentTarget target, DeploymentExecutionContext? context = null, CancellationToken cancellationToken = default)`
- `ApplyAsync(DeploymentPlan plan, IDeploymentTarget target, DeploymentExecutionContext? context = null, CancellationToken cancellationToken = default)`

Phase 1 uses existing abstraction concepts. Analysis found two implementation-blocking contract gaps, so implementation must first add:

- `IArtifactReader.ReadResourcesAsync(CancellationToken)` returning normalized `DeploymentResource` records.
- `DeploymentExecutionContext` carrying optional `DeploymentActor Actor` and `bool Prune`.
- `DeploymentChange.Resource` carrying the desired resource snapshot needed by dry-run and apply.

The execution context is deliberately transport-agnostic. CLI/API/operator slices can populate it later without changing the engine core.

## Resource Handler Contract

Handlers implement `IResourceHandler`.

Handler responsibilities:

- Declare one resource type.
- Read current target state.
- Validate desired resource state.
- Diff desired state against current state.
- Produce dry-run resource results.
- Apply create, update, and delete changes.

Engine responsibilities:

- Register handlers by resource type.
- Reject duplicate handler registrations.
- Route each resource to the matching handler.
- Convert missing handlers to diagnostics instead of exceptions.
- Preserve deterministic ordering.
- Apply `DeploymentExecutionContext.Prune` only during diff planning; pruning defaults to false.

## History Contract

The engine records apply attempts through `IDeploymentHistoryStore`.

Phase 1 implementation provides:

- `InMemoryDeploymentHistoryStore`
- Append-only record behavior
- Find by deployment ID
- List by target descriptor

Dry-run and validation do not write history in Phase 1.

Apply history records actor from `DeploymentExecutionContext.Actor` when supplied.

## Diagnostics

The engine must use `DeploymentDiagnostic`.

Required diagnostic codes:

- `deployment.engine.handler.missing`
- `deployment.engine.handler.duplicate`
- `deployment.engine.resource.duplicate`
- `deployment.engine.artifact.invalid`
- `deployment.engine.plan.invalid`
- `deployment.engine.validate.failed`
- `deployment.engine.read.failed`
- `deployment.engine.diff.failed`
- `deployment.engine.dry-run.failed`
- `deployment.engine.apply.failed`
- `deployment.engine.history.failed`
- `deployment.engine.prune.disabled`

## Status Mapping

Validation:

- Success: `Validated`
- Any error diagnostic: `ValidationFailed`

Dry-run:

- No applyable changes: `NoOp`
- Any create/update/delete changes without error diagnostics: `DryRunCompleted`
- Any blocking diagnostic: `ValidationFailed`

Apply:

- All applyable changes completed: `Applied`
- No applyable changes: `NoOp`
- Some applyable changes failed and some succeeded/skipped: `PartiallyApplied`
- All applyable changes failed: `Failed`

## Non-Goals

The engine contract must not include:

- CLI command models
- HTTP request/response models
- Persistence-specific history entities
- Kubernetes CRDs
- OCI artifact references
- Approval or signature records
- Policy engine decisions
- Workflow runtime-state records
