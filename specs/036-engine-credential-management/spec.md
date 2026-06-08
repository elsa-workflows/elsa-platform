# Feature Specification: Engine Credential Management UI

**Feature Branch**: `codex/036-engine-credential-management`

**Created**: 2026-06-08

**Status**: Draft

**Input**: User description: "Add a Console app UI where workspace administrators can manage engine credential stores and credential references outside the new application setup wizard. The previous engine credential feature made this possible only during setup; users need an obvious workspace-level place to create, inspect, update, rotate, archive, and understand usage for platform-to-engine credentials. Runtime secrets remain managed by runtimes and are out of scope."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find and Manage Engine Credentials (Priority: P1)

A deployment administrator opens the Console and finds a dedicated workspace-level place to manage engine credential stores and references without starting a new application setup flow.

**Why this priority**: Users cannot reliably discover credential store/reference creation when it is only embedded inside the new application wizard. A visible management surface is required before the rest of the credential lifecycle is useful.

**Independent Test**: Can be tested by opening the Console for a selected workspace, navigating to the engine credential management surface, and confirming existing stores and references are listed with safe metadata and clear engine-only scope.

**Acceptance Scenarios**:

1. **Given** a workspace administrator is in the Console, **When** they navigate through deployment-related navigation, **Then** they can open an "Engine credentials" surface without creating or editing an application.
2. **Given** the workspace has active and archived engine credential stores, **When** the administrator views the surface, **Then** active items are shown by default and archived items can be inspected without being offered for new assignments.
3. **Given** the administrator reads the surface labels, **When** they inspect the empty, list, and form states, **Then** it is clear that these credentials are for platform-to-engine communication and not runtime secrets.

---

### User Story 2 - Create and Update Stores and References (Priority: P1)

A deployment administrator creates and edits engine credential stores and credential references from the dedicated management surface, including local encrypted credentials and external locator-only references.

**Why this priority**: Administrators need to prepare credentials before registering engines, and they need to correct labels or locator metadata without repeating setup flows.

**Independent Test**: Can be tested by creating a store and reference for each supported store type, editing safe metadata, and verifying raw credential values are never shown after submission.

**Acceptance Scenarios**:

1. **Given** an administrator has deployment setup permission, **When** they create an engine credential store, **Then** they can choose any supported store type and provide a readable store name.
2. **Given** an active store exists, **When** the administrator creates a credential reference, **Then** they can provide a readable reference name and the appropriate local credential value or external locator for the store type.
3. **Given** a local encrypted credential reference exists, **When** the administrator views or edits it, **Then** the submitted credential value is not displayed and can only be replaced through a rotation action.
4. **Given** an external credential reference exists, **When** the administrator edits it, **Then** they can update safe locator metadata without entering raw secret material.

---

### User Story 3 - Understand Usage Before Lifecycle Actions (Priority: P2)

A deployment administrator sees which engines use a credential reference before rotating, editing, or archiving it.

**Why this priority**: Credential changes can break platform-to-engine communication. Administrators need usage context before disruptive lifecycle actions.

**Independent Test**: Can be tested by assigning a credential reference to multiple engines, viewing the reference usage, and attempting archive or rotation flows that disclose affected engines before confirmation.

**Acceptance Scenarios**:

1. **Given** a credential reference is used by engines, **When** the administrator opens its details, **Then** all affected engines are listed with application and environment context.
2. **Given** a credential reference has usage, **When** the administrator starts an archive or rotation action, **Then** the UI shows affected engines before the action is submitted.
3. **Given** a credential reference has no usage, **When** the administrator starts an archive action, **Then** the UI makes clear that no active engines depend on it.

---

### User Story 4 - Reuse Credentials in Engine Setup (Priority: P2)

A deployment administrator can move between credential management and engine setup so newly created references are immediately usable for engine registration or assignment.

**Why this priority**: The management surface should not strand users away from the deployment flow. Credential preparation and engine assignment are adjacent workflows.

**Independent Test**: Can be tested by creating a reference from the management surface, opening engine registration, and selecting that reference without refreshing or recreating the application.

**Acceptance Scenarios**:

1. **Given** an administrator creates an active credential reference, **When** they register or edit an engine in the same workspace, **Then** the new reference appears as an eligible assignment.
2. **Given** an engine registration screen has no references available, **When** the administrator needs credentials, **Then** the UI offers a clear route to manage engine credentials.
3. **Given** the administrator returns from credential management to engine setup, **When** they select a reference, **Then** only active workspace references are offered.

### Edge Cases

- A workspace has no engine credential stores.
- A workspace has stores but no credential references.
- A user has read permission but lacks deployment setup permission.
- A user attempts to archive a store that still has active references.
- A user attempts to archive or rotate a reference used by many engines.
- A local encrypted reference needs rotation, but the administrator leaves the new credential value blank.
- An archived reference is still assigned to an existing engine.
- A store type cannot be verified by the platform.
- A user confuses engine credential management with runtime secret management.
- A list contains many references across multiple store types.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a dedicated workspace-level Console surface for engine credential stores and credential references outside the new application setup wizard.
- **FR-002**: System MUST make the engine credential management surface discoverable from deployment-related navigation.
- **FR-003**: System MUST show active engine credential stores and credential references by default.
- **FR-004**: System MUST allow authorized administrators to inspect archived stores and references without offering archived items for new engine assignments.
- **FR-005**: System MUST allow authorized administrators to create engine credential stores for all supported store types: local encrypted database, Azure Key Vault, Kubernetes Secrets, environment variable name, and generic external reference.
- **FR-006**: System MUST allow authorized administrators to create credential references under active stores.
- **FR-007**: System MUST allow authorized administrators to edit safe store and reference metadata where existing platform contracts permit edits.
- **FR-008**: System MUST allow authorized administrators to rotate local encrypted credential references without displaying the current protected value.
- **FR-009**: System MUST prevent external-store references from collecting raw secret values.
- **FR-010**: System MUST show only safe metadata in lists, detail views, empty states, confirmations, and errors.
- **FR-011**: System MUST explain that engine credentials are used for platform-to-engine communication and are separate from runtime secrets and artifact secret references.
- **FR-012**: System MUST show credential reference usage with enough context to identify affected engines, applications, and environments before lifecycle actions.
- **FR-013**: System MUST require confirmation before archiving stores or references.
- **FR-014**: System MUST disclose active reference usage before archive or rotation actions that can affect engine communication.
- **FR-015**: System MUST enforce existing workspace deployment setup permissions for create, update, rotate, and archive actions.
- **FR-016**: System MUST provide read-only states for users who can view deployment data but cannot manage setup.
- **FR-017**: System MUST preserve the existing new application setup credential step while allowing the dedicated surface to be the primary place for credential lifecycle management.
- **FR-018**: System MUST provide a clear route from engine registration/editing screens to the engine credential management surface when no usable references exist.
- **FR-019**: System MUST ensure newly created active references are available to engine registration and assignment flows in the same workspace.
- **FR-020**: System MUST remain workspace-scoped; users MUST NOT see or assign credential stores or references from another workspace.

### Key Entities *(include if feature involves data)*

- **Engine Credential Management Surface**: The workspace-level Console area where users list, filter, create, inspect, update, rotate, and archive engine credential stores and references.
- **Engine Credential Store**: Workspace-scoped store metadata for platform-to-engine credentials, including name, type, status, provider label, description, and related references.
- **Engine Credential Reference**: Named credential entry under a store, representing either locally protected credential material or external locator metadata.
- **Credential Usage Summary**: The safe list of engines that use a credential reference, including enough application and environment context to assess impact.
- **Lifecycle Confirmation**: A confirmation state shown before archive or rotation actions when credential usage may affect engine communication.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workspace administrator can find the engine credential management surface from deployment navigation in under 30 seconds during acceptance testing.
- **SC-002**: A workspace administrator can create a store and credential reference for any supported store type without starting a new application setup flow.
- **SC-003**: 100% of credential management list, detail, and confirmation surfaces avoid displaying raw secret values after submission.
- **SC-004**: A workspace administrator can identify every engine using a credential reference before archiving or rotating it.
- **SC-005**: Users without setup permission can view safe credential metadata but cannot submit create, update, rotate, or archive actions.
- **SC-006**: A newly created active reference appears in engine registration or assignment options for the same workspace without requiring a new application setup flow.

## Assumptions

- The existing workspace deployment setup permission remains the authority for credential management mutations.
- Existing workspace deployment APIs for stores, references, rotation, archive, and usage are reused where they already satisfy the required behavior.
- The dedicated surface may be implemented under the existing Deployments area rather than as a global platform settings area.
- Provider browsing and real secret-provider integration remain out of scope; external store types continue to store safe locators only.
- Runtime secret management remains inside runtimes and is not included in this Console management surface.
