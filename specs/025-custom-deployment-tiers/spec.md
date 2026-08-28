# Feature Specification: Custom Deployment Tiers

**Feature Branch**: `025-custom-deployment-tiers`

**Created**: 2026-05-27

**Status**: Draft

**Input**: User description: "Allow workspace admins to configure custom deployment tiers composed from stable coded capabilities, replacing the fixed EnvironmentTier enum semantics with user-defined tier definitions analogous to roles and permissions."

> **Forward compatibility note**: `specs/031-organization-tenancy` places workspaces under organizations. This feature's tier definitions remain workspace-owned; organization-wide shared tier catalogs are still deferred.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Workspace Tiers (Priority: P1)

A workspace admin defines the deployment tiers used by their organization, names them according to local conventions, orders them for cockpit display and promotion flows, and assigns platform-defined tier capabilities that describe the operational meaning of each tier.

**Why this priority**: Custom tiers deliver the main user value: workspaces can model QA, UAT, pre-production, production regions, certification, demos, and other real deployment stages without losing platform-understood semantics.

**Independent Test**: Can be fully tested by creating a workspace tier named "UAT", assigning pre-production and promotion-target capabilities, saving it, and verifying it is available when managing deployment environments.

**Acceptance Scenarios**:

1. **Given** a workspace admin opens deployment tier settings, **When** they create a tier named "UAT" with a description, sort order, and selected coded capabilities, **Then** the tier is saved and appears in the workspace's tier list.
2. **Given** a workspace has existing tiers, **When** an admin edits a tier's name, description, sort order, or capability selection, **Then** future environment setup uses the updated tier definition while existing environment links remain intact.
3. **Given** a non-admin workspace member opens tier settings, **When** they attempt to create or edit a tier, **Then** the system prevents the change and explains that workspace administration permission is required.

---

### User Story 2 - Attach Custom Tiers To Environments (Priority: P2)

A workspace member with deployment setup permission selects one of the workspace's active tiers when creating or editing a deployment environment, so every environment has both a custom label and coded operational meaning.

**Why this priority**: Custom tier definitions only become useful when environments reference them consistently. This preserves environment-level deployment behavior while removing the fixed tier name constraint.

**Independent Test**: Can be fully tested by creating a tier named "Production EU", assigning production-like capabilities, attaching it to an environment, and verifying the environment cockpit shows the custom tier label and inherited tier capabilities.

**Acceptance Scenarios**:

1. **Given** active tiers exist for a workspace, **When** a deployment environment is created, **Then** the creator must select exactly one active tier.
2. **Given** an environment is assigned to "Production EU", **When** the cockpit shows that environment, **Then** it displays "Production EU" as the tier label and treats the environment according to that tier's coded capabilities.
3. **Given** a tier has been archived, **When** a user creates or edits an environment, **Then** the archived tier is not offered for new selections but existing environments using it remain readable.

---

### User Story 3 - Preserve Stable Elsa Control Semantics (Priority: P3)

The platform uses coded tier capabilities to make consistent decisions about deployment risk, promotion eligibility, required confirmation, rollback availability, validation expectations, and observability expectations without depending on tier names.

**Why this priority**: This is the reason to prefer custom tiers plus coded capabilities over arbitrary strings. It preserves safe platform behavior while allowing workspace-specific naming.

**Independent Test**: Can be fully tested by defining two tiers with different names but the same production-like capability and verifying they receive the same production-grade warnings and safeguards.

**Acceptance Scenarios**:

1. **Given** two custom tiers both include a production-like capability, **When** deployments target environments using those tiers, **Then** both targets receive the same production-grade safeguards regardless of tier name.
2. **Given** a tier lacks a promotion-target capability, **When** a user attempts to choose an environment with that tier as a promotion target, **Then** the system prevents or flags the action before deployment.
3. **Given** an admin changes capabilities on a tier used by existing environments, **When** they save the change, **Then** the system presents the operational impact and records the changed tier semantics.

---

### User Story 4 - Migrate Existing Fixed Tiers (Priority: P4)

Existing workspaces that use the fixed Dev, Test, Stage, and Production values continue working after custom tiers are introduced, with equivalent default tier definitions available for review and customization.

**Why this priority**: The feature must not disrupt existing deployment records or user workflows. Migration continuity is required before the fixed values can be retired.

**Independent Test**: Can be fully tested by opening a workspace with Dev, Test, Stage, and Production environments and verifying those environments are assigned to matching default tier definitions with equivalent behavior.

**Acceptance Scenarios**:

1. **Given** an existing workspace has environments using Dev, Test, Stage, and Production, **When** custom tiers become available, **Then** each environment remains assigned to an equivalent tier and keeps its display meaning.
2. **Given** a migrated workspace admin opens tier settings, **When** they view default tiers, **Then** they can rename, reorder, or extend those tiers without losing environment assignments.
3. **Given** a workspace has no custom tier configuration, **When** deployment setup is opened, **Then** a sensible default tier set is available without requiring manual setup first.

### Edge Cases

- A workspace admin attempts to create two active tiers with the same name; the system rejects the duplicate within that workspace.
- A tier is assigned to one or more environments; the system prevents hard deletion and offers archive or rename behavior instead.
- An admin attempts to remove the last active tier from a workspace; the system prevents the change because environments need an assignable tier.
- An admin removes a capability from a tier used by production-like environments; the system requires an impact review before saving.
- A user tries to assign an environment to a tier from another workspace; the system rejects the assignment.
- A migrated environment references an old fixed tier value that has no matching tier definition; the system assigns a safe default and flags the environment for admin review.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow workspace admins to view the workspace's deployment tier definitions.
- **FR-002**: System MUST allow workspace admins to create custom deployment tier definitions with a name, optional description, active or archived status, and display order.
- **FR-003**: System MUST require tier names to be unique among active tiers within the same workspace.
- **FR-004**: System MUST provide a platform-defined catalog of coded tier capabilities that workspace admins can attach to tier definitions.
- **FR-005**: System MUST prevent workspace admins from creating, renaming, or configuring coded capabilities themselves; coded capabilities are stable platform semantics.
- **FR-006**: System MUST allow workspace admins to attach multiple coded capabilities to a custom tier.
- **FR-007**: System MUST require every deployment environment to reference exactly one tier definition from the same workspace.
- **FR-008**: System MUST prevent new environment assignments to archived tiers while preserving existing environments already assigned to those tiers.
- **FR-009**: System MUST prevent hard deletion of any tier that is referenced by an environment or deployment history.
- **FR-010**: System MUST expose the selected tier name and coded capabilities anywhere environment summaries, deployment targets, promotion previews, or deployment warnings depend on tier meaning.
- **FR-011**: System MUST use coded capabilities, not tier names, when determining tier-aware behavior such as production-like safeguards, promotion eligibility, confirmation expectations, rollback support, secret verification expectations, and observability expectations.
- **FR-012**: System MUST show workspace admins an impact summary before saving tier capability changes that affect environments already using that tier.
- **FR-013**: System MUST record who changed a tier definition, what changed, and when the change occurred.
- **FR-014**: System MUST create sensible default tier definitions for workspaces that do not yet have custom tiers.
- **FR-015**: System MUST map existing Dev, Test, Stage, and Production environment values to equivalent default tier definitions without requiring user action.
- **FR-016**: System MUST preserve existing environment assignments, deployment history, promotion previews, and cockpit visibility during and after the transition to custom tiers.
- **FR-017**: System MUST prevent users without workspace administration permission from creating, editing, archiving, deleting, or changing capabilities on tier definitions.
- **FR-018**: System MUST allow users with deployment setup permission to select an active workspace tier when creating or editing an environment.
- **FR-019**: System MUST clearly distinguish a tier's user-defined label from its coded capabilities in administrative views.
- **FR-020**: System MUST support multiple active tiers with the same coded capability set when their names are different, such as "Production EU" and "Production US".

### Key Entities *(include if feature involves data)*

- **Deployment Tier Definition**: Workspace-owned tier profile selected by environments. Key attributes include name, description, active or archived status, display order, default indicator, creation metadata, and update metadata.
- **Tier Capability**: Stable platform-defined semantic bit that describes tier behavior, such as production-like, promotion source, promotion target, confirmation required, rollback enabled, secret verification required, or observability required.
- **Tier Capability Assignment**: Association between a deployment tier definition and one coded tier capability.
- **Deployment Environment Tier Reference**: The relationship from a deployment environment to exactly one deployment tier definition in the same workspace.
- **Tier Change Record**: Audit record describing tier definition changes, capability changes, actor, timestamp, and affected environments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workspace admin can create a custom deployment tier with at least three coded capabilities in under 2 minutes.
- **SC-002**: A workspace admin can rename and reorder existing default tiers without breaking any existing environment assignments.
- **SC-003**: 100% of existing environments using Dev, Test, Stage, or Production retain equivalent tier labels and semantics after migration.
- **SC-004**: Users can distinguish tier labels from coded capabilities in administrative views without consulting documentation.
- **SC-005**: Attempts to assign an environment to a missing, archived, or cross-workspace tier are rejected before the environment is saved.
- **SC-006**: Tier-aware deployment safeguards behave identically for two tiers that share the same coded capabilities, even when their names differ.
- **SC-007**: A workspace with at least 20 tiers and 250 environments remains understandable to users through sorted tier lists and environment summaries.

## Assumptions

- Workspace admins are the only users who can manage tier definitions and tier capability assignments.
- Users with deployment setup permission can assign an existing active tier to an environment but cannot change tier definitions unless they are also workspace admins.
- Coded tier capabilities are owned by the platform and are not workspace-created.
- The initial coded capability catalog will cover the semantics currently needed by deployment setup, promotion, confirmation, rollback, validation, drift, and observability workflows.
- Existing fixed tier values remain readable during transition until all environments reference tier definitions.
- Custom tier configuration is workspace-scoped; tiers are not shared across workspaces in this feature.
