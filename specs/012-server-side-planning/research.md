# Research: Server-Side Planning

## Decision: Deterministic Rule-Based Planner

Rationale: Manifest dependencies, infrastructure requirements, and compatibility metadata are explicit. A deterministic planner is testable and auditable.

Alternatives considered: AI/natural-language planner, rejected for this phase.

## Decision: Shared Planner Service For Plan, Resolve, And Bundle

Rationale: One service prevents divergence between validation and generated output.

Alternatives considered: separate resolver and bundle validation, rejected because duplication is the problem being solved.
