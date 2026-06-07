# Feature Specification: Runtime Kind Compatibility

**Feature Branch**: `032-runtime-kind-compatibility`

**Created**: 2026-06-04

**Status**: Draft

**Input**: User description: "Package manifests declare compatible runtime kinds for Elsa Server and Elsa Studio using open-ended string runtime kind values, available at package and feature compatibility levels."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Filter Packages By Target Runtime (Priority: P1)

A workspace user building an Elsa application sees only packages and features that apply to the type of application they are building, such as an Elsa Server app or an Elsa Studio app.

**Why this priority**: Runtime Builder and future Studio package experiences must prevent users from accidentally selecting packages that cannot run in the target application.

**Independent Test**: Can be tested by publishing package metadata for both server-only and studio-only packages, opening a catalog experience for each target runtime, and confirming that incompatible options are hidden or clearly unavailable.

**Acceptance Scenarios**:

1. **Given** a catalog contains one Elsa Server-compatible package and one Elsa Studio-compatible package, **When** a user builds an Elsa Server application, **Then** only the server-compatible package is selectable by default.
2. **Given** a package contains both server-compatible and studio-compatible features, **When** a user builds an Elsa Studio application, **Then** only the studio-compatible features are selectable by default.
3. **Given** a user views package details, **When** the package or feature is not compatible with the current target runtime, **Then** the experience explains the mismatch without implying the package is broken.

---

### User Story 2 - Declare Runtime Applicability In Manifests (Priority: P2)

A package author declares which kind of Elsa application a package or feature supports, using stable runtime-kind identifiers that are not limited to a closed product enumeration.

**Why this priority**: Package authors need a clear contract before Elsa Studio packages and third-party host-specific packages can be safely listed in the same catalog.

**Independent Test**: Can be tested by validating sample manifests that declare official Elsa runtime kinds and custom runtime kinds, then confirming the catalog preserves and exposes those declarations.

**Acceptance Scenarios**:

1. **Given** a package supports only Elsa Server, **When** the author declares the package-level runtime kind, **Then** all features inherit that compatibility unless a feature declares its own runtime kind compatibility.
2. **Given** a package contains features for different application kinds, **When** individual features declare runtime kind compatibility, **Then** each feature is evaluated using its own declaration.
3. **Given** a package author uses a custom runtime kind identifier, **When** the manifest is validated, **Then** the value is accepted if it follows the runtime-kind identifier rules.

---

### User Story 3 - Preserve Existing Package Behavior (Priority: P3)

Existing package manifests that do not declare runtime kind compatibility continue to behave as Elsa Server packages until authors update them.

**Why this priority**: Runtime-kind compatibility should not break the current server package catalog or force immediate republishing of all existing packages.

**Independent Test**: Can be tested by syncing existing manifests without runtime-kind declarations and confirming they remain visible in Elsa Server package experiences while new Studio experiences do not treat them as Studio-compatible by default.

**Acceptance Scenarios**:

1. **Given** an existing package manifest has no runtime-kind declaration, **When** the package is synced, **Then** it remains compatible with Elsa Server catalog experiences.
2. **Given** an existing package manifest has no runtime-kind declaration, **When** a Studio package experience filters for Elsa Studio compatibility, **Then** the package is not shown as Studio-compatible by default.
3. **Given** an author updates a package to declare Elsa Studio compatibility, **When** the updated package is synced, **Then** Studio package experiences can include it according to the declared compatibility.

### Edge Cases

- A package declares both package-level and feature-level runtime kinds; feature-level declarations take precedence for that feature.
- A package declares no features; package-level runtime compatibility still applies to package discovery and package details.
- A feature declares an empty runtime-kind list; the manifest is rejected or the feature is treated as invalid because the declaration carries no usable compatibility meaning.
- A runtime-kind value differs only by letter casing from another value; matching is case-insensitive, while display should preserve the canonical value chosen by the publisher.
- A manifest declares a malformed runtime-kind identifier; the manifest is rejected with an actionable validation finding.
- A runtime target is unknown to the consuming application; packages and features for that target are preserved in the catalog but not selected for known Elsa Server or Elsa Studio flows.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow package manifests to declare one or more compatible runtime kinds at the package compatibility level.
- **FR-002**: System MUST allow package features to declare one or more compatible runtime kinds at the feature compatibility level.
- **FR-003**: System MUST treat feature-level runtime-kind declarations as overriding package-level declarations for that feature.
- **FR-004**: System MUST treat a feature with no feature-level runtime-kind declaration as inheriting the package-level runtime-kind declaration.
- **FR-005**: System MUST treat an existing manifest with no package-level or feature-level runtime-kind declarations as compatible with Elsa Server for existing server package experiences.
- **FR-006**: System MUST NOT treat manifests without runtime-kind declarations as compatible with Elsa Studio by default.
- **FR-007**: System MUST define official runtime-kind identifiers for Elsa Server and Elsa Studio.
- **FR-008**: System MUST allow custom runtime-kind identifiers so third-party or future Elsa application hosts can participate without a schema change.
- **FR-009**: System MUST validate runtime-kind identifiers for non-empty, stable, machine-readable values and reject blank or malformed values.
- **FR-010**: System MUST expose effective package and feature runtime compatibility to catalog consumers so package lists, package details, and builder experiences can filter or explain compatibility.
- **FR-011**: System MUST keep runtime kind separate from runtime capabilities; runtime kind identifies the application host type, while capabilities identify supported behavior within a compatible host.
- **FR-012**: System MUST provide clear compatibility diagnostics when a package or feature is incompatible with the selected target runtime.
- **FR-013**: System MUST preserve unknown but valid runtime-kind values during ingest, storage, and catalog projection.
- **FR-014**: System MUST document the compatibility inheritance and defaulting rules for package authors and catalog consumers.

### Key Entities *(include if feature involves data)*

- **Runtime Kind**: A stable machine-readable identifier for the kind of application host a package or feature supports, such as Elsa Server, Elsa Studio, or a future/custom host.
- **Package Compatibility Declaration**: Package-level metadata that defines the default runtime kinds supported by the package.
- **Feature Compatibility Declaration**: Feature-level metadata that defines runtime kinds for a specific feature and overrides the package default for that feature.
- **Effective Runtime Compatibility**: The resolved compatibility result used by catalog and builder experiences after applying feature overrides, package defaults, and backward-compatibility rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A catalog containing mixed Elsa Server and Elsa Studio package metadata can return the correct compatible package and feature set for each target runtime in a single catalog query or equivalent user action.
- **SC-002**: Existing package manifests without runtime-kind declarations remain visible in Elsa Server package experiences after synchronization.
- **SC-003**: Studio-targeted catalog experiences exclude existing undeclared server packages unless those packages explicitly declare Elsa Studio compatibility.
- **SC-004**: Manifest authors can validate official and custom runtime-kind declarations before publishing, with invalid declarations producing actionable errors.
- **SC-005**: Package detail and builder experiences can explain runtime incompatibility using catalog metadata without requiring package code execution.

## Assumptions

- Elsa Server and Elsa Studio are the first official runtime kinds.
- Runtime-kind values are stable identifiers intended for machines, not localized display labels.
- Existing generated package manifests currently represent Elsa Server packages unless they explicitly state otherwise in the future.
- Per-runtime version ranges are out of scope for this feature unless introduced later through a separate compatibility enhancement.
- Runtime-kind compatibility affects package and feature discovery, selection, and diagnostics; it does not by itself install, configure, or execute packages.
