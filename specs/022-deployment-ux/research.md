# Research: Deployment UX

## Decision: Keep deployment workspace orchestration in Deployment.Core

**Rationale**: The deployment cockpit, engine registration, validation, run tracking, rollback, and runtime controls are deployment-domain behavior. Keeping them in `ValenceControl.Deployment.Core` avoids turning package catalog services into the deployment product layer while still allowing the API and persistence projects to adapt the services to the existing platform host.

**Alternatives considered**:

- Put models in PackageCatalog.Core: rejected because deployment would become coupled to catalog ownership concepts beyond workspace IDs.
- Put everything in API endpoints: rejected because tests and future non-HTTP consumers would have no reusable service boundary.

## Decision: Persist through the existing catalog EF database first

**Rationale**: Account, workspace, membership, entitlements, package sources, and runtime configurations already persist in the catalog database. Using the same database for workspace deployment records keeps workspace ownership, migrations, and test infrastructure consistent for this slice.

**Alternatives considered**:

- Add a new deployment database: rejected for the first slice because it adds operational complexity before the model is proven.
- Keep in-memory storage: rejected because it cannot satisfy real UX, history, rollback, isolation, or refresh requirements.

## Decision: Store secret references only

**Rationale**: The identity and tenancy plan already requires customer-facing APIs not to expose raw secrets. Deployment engine credentials and runtime secrets must stay provider-backed so desired state, audit history, and console state remain safe.

**Alternatives considered**:

- Store encrypted secret values directly in deployment tables: deferred because provider-backed references are enough for the first UX and avoid key-management scope.
- Store values in source-controlled desired state: rejected by constitution and spec safety requirements.

## Decision: Use capability IDs as the runtime-control gate

**Rationale**: The UX must not show generic restart operations. A stable capability ID lets the API and console distinguish workflow, engine API, shell, and hosting boundaries while rejecting direct unsupported control requests.

**Alternatives considered**:

- Hard-code control buttons by engine health: rejected because health does not imply operation support.
- Always expose common controls: rejected because engines and hosting providers differ and unsupported operations must fail closed.

## Decision: Use flexible workspace permission grants for deployment actions

**Rationale**: Deployment setup, desired-state management, promotion preview, deploy, rollback, runtime controls, observability management, and read access need to be independently assignable. Hard-coding these actions to the current `Owner`, `SourceAdmin`, and `Reader` roles would create rework as soon as deployment-specific responsibilities emerge.

**Alternatives considered**:

- Reuse existing roles only: rejected because it cannot distinguish deployment setup from deployment execution or runtime controls.
- Add only a `Deployer` role: rejected because it still combines setup, execution, rollback, and controls.
- Full organization RBAC suite: deferred because this feature only needs workspace-scoped permission grants.

## Decision: Execute deployments as durable queued runs with an in-process worker first

**Rationale**: Users need running status, page-refresh persistence, active-run conflict handling, and recovery behavior. Durable queued runs provide those semantics while allowing the first worker to run in-process before external orchestration is introduced.

**Alternatives considered**:

- Synchronous request execution: rejected because long-running deployments would tie up API requests and would not survive refresh/restart well.
- Record-only manual runs: rejected because it would not implement real deployment UX.
- External orchestrator first: deferred because provider/orchestrator integration would obscure the workspace workflow foundation.

## Decision: Store desired state as structured platform records first

**Rationale**: The first UX needs editable, comparable platform-owned desired state without requiring manifest/artifact import/export or GitOps flows. Structured records make validation, diffing, and UI editing testable while preserving a future path to manifest/artifact export.

**Alternatives considered**:

- Manifest/artifact import only: rejected because it blocks the console setup and edit flow.
- JSON blob only: rejected because it makes meaningful diff/validation and future editing harder.
- Both structured records and manifest import now: deferred to keep the first slice bounded.

## Decision: Treat observability and drift as persisted metadata in this slice

**Rationale**: Cockpit views can show configured observability bindings, provider status, correlated revision, and known drift reports without requiring telemetry provider credentials, network calls, or provider-specific query code. Live drift detection and telemetry pulls can follow after the deployment workflow is durable.

**Alternatives considered**:

- Live logs/traces/metrics queries now: deferred because provider integration and credentials are substantial scope.
- Remove observability/drift entirely: rejected because the cockpit model and UX need to reserve the governance surface.
- Drift detection only: rejected because observability binding metadata is also part of the cockpit contract.

## Decision: Require explicit single-user confirmation for risky actions

**Rationale**: Deploy, rollback, and runtime controls are risky mutations. Requiring the initiating authorized user to explicitly confirm provides an auditable safety boundary without introducing a full approval workflow.

**Alternatives considered**:

- Plain button click only: rejected because accidental actions are too easy.
- Production-only approval records: deferred until environments and approval policy mature.
- Multi-party approvals for all risky actions: deferred as a separate governance feature.

## Decision: First apply path may use a local/fake adapter

**Rationale**: The feature needs durable queued runs, validation, history, rollback, and UX wiring before provider-specific deployment adapters are ready. A testable local adapter lets the platform execute and audit the control-plane workflow without blocking on Kubernetes, Azure, OCI, or GitOps integration.

**Alternatives considered**:

- Wait for production cloud adapters before any deploy UX: rejected because it delays validation of core workflow and console behavior.
- Make apply a no-op forever: rejected because users need queued run state and rollback semantics to be represented honestly.

## Decision: One active deployment run per target environment

**Rationale**: Concurrent deployment to the same environment can corrupt deployed-revision state and history semantics. A simple active-run conflict rule is understandable and sufficient for the first slice.

**Alternatives considered**:

- Allow concurrent runs by engine: deferred until multi-engine environment semantics are clearer.
- Queue all runs without environment locking: rejected because queued execution still needs target-environment concurrency guarantees.
