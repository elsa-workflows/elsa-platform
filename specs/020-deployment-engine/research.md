# Research: Deployment Engine MVP

## Decision: Build The Engine Around Existing Abstractions First

**Rationale**: `Elsa.Platform.Deployment.Abstractions` already defines `IDeploymentEngine`, resource handlers, resource state, plans, results, targets, artifacts, diagnostics, and history. Using these contracts validates whether the abstractions are sufficient before changing public APIs.

**Alternatives considered**:

- Create engine-specific request/result types immediately. Rejected because it would duplicate existing abstractions and obscure real contract gaps.
- Use manifest/artifact package concrete types directly. Rejected because the engine must stay transport- and packaging-agnostic.

## Decision: Resource Handlers Are The Phase 1 Extension Boundary

**Rationale**: Resource-specific validation, state reading, dry-run behavior, and apply behavior differ by resource type. `IResourceHandler` already carries this responsibility and keeps product-specific handlers out of the core engine.

**Alternatives considered**:

- Separate validator, state reader, differ, and applier registries. Rejected for Phase 1 because it adds orchestration complexity before handler needs are proven.
- Hard-code workflow and recipe logic in the engine. Rejected because it prevents third-party deployable resources and violates bounded subsystem goals.

## Decision: Dry-Run Produces Plans Without History Mutation

**Rationale**: Dry-run is a preview and must be safely repeatable. History is reserved for apply attempts in Phase 1 so automation can distinguish planned changes from executed changes.

**Alternatives considered**:

- Record every dry-run in history. Deferred because this is an audit/governance feature better addressed with durable history and API/CLI context.

## Decision: Apply Is Non-Transactional In Phase 1

**Rationale**: Deployable resources may be heterogeneous and handled by different systems. Phase 1 should record partial failures and retryability rather than claim rollback semantics it cannot guarantee.

**Alternatives considered**:

- Require all handlers to support rollback. Deferred to later platform engineering phases after resource handler capabilities are proven.
- Stop on first failure without recording later resource statuses. Rejected because history must support recovery and troubleshooting.

## Decision: In-Memory History Store Only

**Rationale**: The engine needs a history abstraction and test implementation to complete the loop without prematurely choosing persistence schema, tenancy model, retention, or audit storage.

**Alternatives considered**:

- Entity Framework history store in this slice. Deferred to enterprise maturity scope.
- File-based history. Rejected because it creates a persistence choice without clear product requirements.

## Decision: Deterministic Ordering By Resource Identity

**Rationale**: Dry-run and apply results must be stable across repeated runs for automation, tests, and future CLI/API output. Resource identity string ordering is sufficient for Phase 1.

**Alternatives considered**:

- Preserve manifest order. Rejected because manifest order may change without semantic changes.
- Topological dependency ordering. Deferred until dependency semantics affect real handlers.
