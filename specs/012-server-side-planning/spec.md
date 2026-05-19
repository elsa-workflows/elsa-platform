# Feature Specification: Server-Side Planning

**Feature Branch**: `012-server-side-planning`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Move dependency closure, infrastructure requirements, default provider selection, and readiness validation from Lovable/browser logic into platform planner APIs."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resolve Builder Intent Into A Planned Runtime (Priority: P1)

A builder client submits user intent and receives a resolved runtime plan containing selected image, packages, features, infrastructure, auto-added items, and findings.

**Independent Test**: Submit an intent with a selected capability/feature and verify required packages/features are auto-added.

**Acceptance Scenarios**:

1. **Given** selected features declare package dependencies, **When** planning runs, **Then** missing required packages/features are returned as auto-added or blocking findings.
2. **Given** selected features require infrastructure, **When** planning runs, **Then** default providers are selected when safe.

---

### User Story 2 - Use Planner Across Resolve And Bundle (Priority: P1)

Resolve and bundle generation use the same planning result so frontend, validation, and generated files cannot diverge.

**Independent Test**: Submit the same intent to plan, resolve, and bundle and verify findings and resolved state are consistent.

**Acceptance Scenarios**:

1. **Given** a missing required setting, **When** plan and bundle are requested, **Then** both report the same blocking finding.
2. **Given** planner auto-adds infrastructure, **When** bundle generation runs, **Then** generated files use the planned infrastructure.

---

### User Story 3 - Reduce Frontend Authority To Presentation (Priority: P2)

Lovable stores user intent and renders resolved state from backend planning instead of computing authoritative dependency closure.

**Independent Test**: Frontend can remove local closure/autofill authority and still display resolved package/feature/infrastructure state.

**Acceptance Scenarios**:

1. **Given** user selections change, **When** the frontend calls plan, **Then** it renders server-resolved additions and findings.

### Edge Cases

- Dependencies form a cycle.
- Multiple providers can satisfy the same infrastructure requirement.
- A selected provider no longer exists.
- Auto-add would select a package unavailable to the caller.
- Required settings are missing or secret.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose a planner API for builder intent.
- **FR-002**: System MUST apply package and feature dependency closure.
- **FR-003**: System MUST derive infrastructure requirements from selected features.
- **FR-004**: System MUST select safe default infrastructure providers when unambiguous.
- **FR-005**: System MUST return resolved state, auto-added items, and findings.
- **FR-006**: Resolve and bundle generation MUST use planner logic.
- **FR-007**: System MUST detect dependency cycles, conflicts, missing packages, missing settings, and image incompatibility.
- **FR-008**: Frontend planning logic MUST become advisory/presentation-only after migration.

### Key Entities

- **Builder Intent**: User-authored desired runtime shape.
- **Runtime Plan**: Server-resolved packages, features, infrastructure, settings, and findings.
- **Auto-Added Item**: Package, feature, or infrastructure selected by planner.
- **Planner Finding**: Structured error/warning/info.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Planner resolves dependency closure for representative package/feature fixtures.
- **SC-002**: Planner derives infrastructure requirements and default providers for common features.
- **SC-003**: Resolve and bundle return findings consistent with planner output.
- **SC-004**: Frontend can render server-resolved state without local authoritative closure.

## Assumptions

- Capability taxonomy can initially map to features/packages using existing manifest metadata and curated mappings.
- Full natural-language intent parsing is out of scope.
