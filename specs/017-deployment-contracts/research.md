# Research: Deployment Foundation Contracts

## Decision: Keep This Slice To Abstractions Only

**Rationale**: The deployment roadmap depends on a shared vocabulary before manifest parsing, artifact IO, engine orchestration, API endpoints, and CLI commands can align. A small abstractions package is independently testable and limits contract churn.

**Alternatives considered**:

- Build manifest parsing in the same slice. Rejected because schema details should depend on the stabilized resource identity model.
- Build artifact folder/ZIP IO in the same slice. Rejected because artifact metadata and digest contracts should be tested first.
- Build a minimal engine in the same slice. Rejected because the engine would force unvalidated choices for target state readers, handlers, history, and partial failure semantics.

## Decision: Base Library Only For Deployment Abstractions

**Rationale**: These contracts need to be consumed by CLI, API, engine, operator, GitOps, test fixtures, and third-party resource packages. Depending only on the base class library keeps the package portable and avoids pulling hosting, persistence, API, catalog implementation, or runtime implementation details into every consumer.

**Alternatives considered**:

- Reference Microsoft.Extensions abstractions. Deferred until concrete DI/logging integration is required by implementation packages.
- Reference Package Catalog abstractions directly. Deferred for this slice; package requirement validation can be added in a later descriptor-validation increment if needed.

## Decision: Prefer Immutable Record Types For Values

**Rationale**: Resource identities, artifact identities, diagnostics, plans, changes, and history records should be deterministic, easy to compare in tests, and safe to pass between pipeline stages without accidental mutation.

**Alternatives considered**:

- Mutable classes. Rejected for foundation value contracts because accidental mutation would make dry-run/apply consistency harder to prove.
- Interfaces for all data shapes. Rejected because simple immutable values are easier to serialize, inspect, compare, and evolve during Phase 1.

## Decision: Model Deletion As Explicit Resource Behavior

**Rationale**: Phase 1 must be conservative. A missing resource in a manifest should not imply deletion. Deletion behavior is represented explicitly so destructive operations require deliberate manifest intent and handler support.

**Alternatives considered**:

- Implicit pruning by default. Rejected as too risky for first deployment loops.
- No deletion model. Rejected because the plan/change taxonomy needs to represent unsupported or future delete operations clearly.

## Decision: Represent Partial Failure Per Resource

**Rationale**: Phase 1 resume semantics are reapply-based. The engine must eventually know which resources succeeded, failed, were skipped, or remain retryable. Recording per-resource operation status now prevents ambiguity later.

**Alternatives considered**:

- Single deployment status only. Rejected because it cannot explain partial application safely.
- Transaction rollback contracts. Deferred because Phase 1 does not promise distributed rollback.

## Decision: Boundary Tests Use Project Metadata And Public Contract Vocabulary

**Rationale**: The repository is early enough that simple project-reference and source vocabulary tests can catch the most dangerous coupling mistakes with minimal ceremony.

**Alternatives considered**:

- Introduce a full architecture-testing package. Deferred until the solution has more deployment projects and richer dependency graphs.
- Rely on code review only. Rejected because control-plane/data-plane separation is a constitutional requirement.
