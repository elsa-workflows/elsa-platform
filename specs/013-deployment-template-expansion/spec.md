# Feature Specification: Deployment Template Expansion

**Feature Branch**: `013-deployment-template-expansion`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Extend server-side bundle generation from Docker Compose to additional deployment template targets such as Azure Container Apps and Kubernetes/Helm."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose Bundle Template Target (Priority: P1)

A builder user can choose a deployment target template and receive files for that target.

**Acceptance Scenarios**:

1. **Given** no target is specified, **When** bundle generation runs, **Then** Docker Compose remains the default.
2. **Given** a supported target is specified, **When** bundle generation runs, **Then** returned files match that target.

---

### User Story 2 - Generate Azure Container Apps Template (Priority: P2)

A professional .NET team can generate Azure Container Apps deployment files from the same resolved runtime plan.

**Acceptance Scenarios**:

1. **Given** a valid planned runtime, **When** Azure Container Apps target is selected, **Then** the response includes infrastructure template files and README instructions.

---

### User Story 3 - Generate Kubernetes Or Helm Template (Priority: P3)

An enterprise platform team can generate Kubernetes/Helm-ready files from the same resolved runtime plan.

**Acceptance Scenarios**:

1. **Given** Kubernetes/Helm target is selected, **When** generation runs, **Then** files include deployment, service, configuration, and README output.

### Edge Cases

- Target is unsupported.
- Selected runtime image does not support the chosen target.
- Infrastructure provider cannot be represented in the target.
- Required secret values must be externalized.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support a target selector for bundle generation.
- **FR-002**: Docker Compose MUST remain the default target.
- **FR-003**: System MUST generate target-specific files server-side.
- **FR-004**: All template targets MUST use the same resolved runtime plan.
- **FR-005**: System MUST return findings when a runtime shape cannot be represented for a target.
- **FR-006**: Generated templates MUST include README instructions.
- **FR-007**: Secret values MUST be externalized as placeholders or platform-native secret references.

### Key Entities

- **Deployment Template Target**: Docker Compose, Azure Container Apps, or Kubernetes/Helm.
- **Template Bundle**: Generated file set for one target.
- **Target Capability**: Whether a runtime image/provider can be represented by a target.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Docker Compose output remains unchanged by default target behavior.
- **SC-002**: Azure template generation returns all documented files for representative fixtures.
- **SC-003**: Kubernetes/Helm generation returns all documented files for representative fixtures.
- **SC-004**: Unsupported target/runtime combinations return findings and no misleading files.

## Assumptions

- Live deployment is out of scope.
- Azure Container Apps comes before Kubernetes/Helm if implementation must be staged.
