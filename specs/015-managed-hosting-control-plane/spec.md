# Feature Specification: Managed Hosting Control Plane

**Feature Branch**: `015-managed-hosting-control-plane`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Provide managed Elsa runtime hosting from saved runtime configurations after the deployment model is proven."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Provision Managed Runtime (Priority: P1)

A customer can provision a managed Elsa runtime from a saved configuration in one supported region and infrastructure shape.

**Acceptance Scenarios**:

1. **Given** a valid saved configuration, **When** managed hosting is provisioned, **Then** a managed runtime environment record is created and reaches a ready or failed state.

---

### User Story 2 - Control Runtime Lifecycle (Priority: P1)

A customer can view, stop, restart, and delete their managed runtime.

**Acceptance Scenarios**:

1. **Given** a managed runtime exists, **When** lifecycle action is requested, **Then** status and audit history are updated.

---

### User Story 3 - Access Runtime Endpoint (Priority: P2)

A customer can see the managed runtime URL and health state.

**Acceptance Scenarios**:

1. **Given** runtime is ready, **When** details are fetched, **Then** URL and health are returned.

### Edge Cases

- Provisioning fails after partial infrastructure creation.
- Customer deletes runtime with persistent data.
- Runtime configuration becomes invalid after provisioning.
- Capacity is unavailable in the only supported region.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provision managed runtime environments from saved configurations.
- **FR-002**: System MUST support one region, one infrastructure shape, and one persistence provider for MVP.
- **FR-003**: System MUST track environment status and lifecycle events.
- **FR-004**: System MUST allow stop, restart, and delete lifecycle actions.
- **FR-005**: System MUST expose runtime URL and health state.
- **FR-006**: System MUST isolate tenants.
- **FR-007**: System MUST avoid broad managed hosting promises outside the supported shape.

### Key Entities

- **Managed Runtime Environment**
- **Runtime Instance**
- **Managed Infrastructure Resource**
- **Lifecycle Event**

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A supported saved configuration can provision one managed runtime.
- **SC-002**: Runtime lifecycle state is visible after every action.
- **SC-003**: Tenant isolation boundaries are represented and tested.
- **SC-004**: Failed provisioning records enough status for operator follow-up.

## Assumptions

- Billing, custom domains, SLAs, multi-region, and enterprise compliance are out of scope for MVP.
