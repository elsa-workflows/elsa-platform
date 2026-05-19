# Feature Specification: Saved Runtime Configurations

**Feature Branch**: `011-saved-runtime-configurations`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Let authenticated users save, reopen, edit, clone, version, and regenerate Elsa Runtime Builder configurations while anonymous builder use remains local/browser-only."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Save And Reopen Runtime Configuration (Priority: P1)

An authenticated user can save a named runtime configuration and reopen it later with the same selected image, packages, features, settings, sources, infrastructure, and local package options.

**Why this priority**: Saved configurations turn the builder from a one-time download tool into a reusable platform product.

**Independent Test**: Save a configuration, fetch it, and verify the returned intent matches the submitted builder state.

**Acceptance Scenarios**:

1. **Given** an authenticated workspace member, **When** they save a valid runtime configuration, **Then** the platform stores it under the selected workspace.
2. **Given** a saved configuration exists, **When** the owner reopens it, **Then** all selected runtime intent fields are returned.
3. **Given** an anonymous builder user, **When** they use the builder, **Then** they can continue using local browser state without a saved backend record.

---

### User Story 2 - Clone And Edit Configuration (Priority: P1)

An authenticated user can clone a saved runtime configuration, rename it, edit it, and generate a bundle from the edited copy.

**Why this priority**: Cloning supports experimentation without damaging a known-good runtime shape.

**Independent Test**: Clone a saved configuration, change one field, and verify the original remains unchanged.

**Acceptance Scenarios**:

1. **Given** a saved configuration, **When** the user clones it, **Then** a distinct configuration is created with copied intent.
2. **Given** the clone is edited, **When** both records are fetched, **Then** only the clone reflects the edit.
3. **Given** the clone is valid, **When** bundle generation is requested, **Then** the same bundle contract is used as ad hoc generation.

---

### User Story 3 - Create Explicit Configuration Versions (Priority: P2)

An authenticated user can create named snapshots so selected package versions and generated lock hashes are preserved for later review.

**Why this priority**: Versioning makes saved runtime configurations reviewable and prepares for deployment and managed hosting.

**Independent Test**: Create a version snapshot, modify the draft, and verify the snapshot remains immutable.

**Acceptance Scenarios**:

1. **Given** a saved configuration draft, **When** a version is created, **Then** the current intent is stored as an immutable snapshot.
2. **Given** the draft changes later, **When** the version is fetched, **Then** it still shows the original selected image and package versions.

### Edge Cases

- A user tries to access a configuration in a workspace they do not belong to.
- A saved configuration references a package source that is no longer visible.
- A saved configuration references package versions that are later hidden, rejected, or invalid.
- A configuration name is empty or duplicates another configuration in the same workspace.
- A version snapshot is requested for an invalid draft.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow authenticated workspace members to create saved runtime configurations.
- **FR-002**: System MUST store full runtime builder intent for saved configurations.
- **FR-003**: System MUST allow workspace members to list, fetch, update, delete, and clone configurations in their workspace.
- **FR-004**: System MUST keep anonymous builder state browser/local only.
- **FR-005**: System MUST support bundle generation from a saved configuration using the same bundle service contract as ad hoc generation.
- **FR-006**: System MUST support explicit immutable configuration version snapshots.
- **FR-007**: System MUST preserve selected image tag, package versions, feature settings, source IDs, and infrastructure selections in snapshots.
- **FR-008**: System MUST enforce workspace membership for all configuration reads and writes.
- **FR-009**: System MUST return findings when saved intent references now-inaccessible sources or packages.

### Key Entities

- **Runtime Configuration**: Mutable saved draft intent under a workspace.
- **Runtime Configuration Version**: Immutable snapshot of a configuration.
- **Bundle Generation Reference**: Optional non-secret metadata linking a generated bundle result to a configuration/version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authenticated users can save and reopen a configuration with no loss of selected intent fields.
- **SC-002**: Cloning creates a separate editable configuration without mutating the source.
- **SC-003**: Version snapshots remain unchanged after later draft edits.
- **SC-004**: Unauthorized workspace callers cannot read or mutate configurations.
- **SC-005**: Bundle generation from saved configuration returns the same file/finding contract as ad hoc generation.

## Assumptions

- Existing workspace identity and membership model is reused.
- Organization RBAC, billing, collaboration, and deployment environments are out of scope.
- Versions are explicit snapshots, not automatic on every save.
