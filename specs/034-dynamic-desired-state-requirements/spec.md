# Feature Specification: Dynamic Desired-State Requirements

**Feature Branch**: `034-dynamic-desired-state-requirements`

**Created**: 2026-06-07

**Status**: Draft

**Input**: User description: "Desired-state revision creation must show only requirement inputs that apply to the current environment tier. Production-only observability controls should not appear on Dev unless the user arrived from a validation action that explicitly asks them to add such a record. Requirements attached to environment tiers should drive the form dynamically for observability and future desired-state requirements."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hide Irrelevant Requirements (Priority: P1)

A deployment operator creates a desired-state revision for a Dev or Test environment and sees only the fields needed for that environment, without production-only observability prompts.

**Why this priority**: Irrelevant requirements confuse users about what the current environment needs and make the desired-state form appear arbitrary.

**Independent Test**: Can be fully tested by opening a new revision page for an environment whose tier does not include observability requirements and verifying no observability input is displayed while revision creation still works.

**Acceptance Scenarios**:

1. **Given** a Dev environment whose tier lacks an observability requirement, **When** a user opens the new revision page, **Then** the page states that no additional desired-state records are required and does not show the observability binding editor.
2. **Given** a user creates a Dev revision from an artifact, **When** they submit the form, **Then** the submitted records include the artifact record and omit observability unless the user explicitly added it through a contextual action.

---

### User Story 2 - Show Required Tier Records (Priority: P2)

A deployment operator creates a desired-state revision for an environment whose tier requires additional desired-state records and sees those requirements clearly in the form.

**Why this priority**: Required tier records must be discoverable before validation blocks a promotion or deployment.

**Independent Test**: Can be fully tested by opening a new revision page for a Production-like environment whose tier includes observability requirements and verifying the observability editor is visible, required, and submitted with the revision.

**Acceptance Scenarios**:

1. **Given** a Production environment whose tier includes an observability requirement, **When** a user opens the new revision page, **Then** the page shows an observability binding section marked as required by that tier.
2. **Given** the required observability section is shown, **When** the user submits without provider or scope, **Then** the system prevents submission and explains which required fields are missing.
3. **Given** the user completes the required observability fields, **When** they submit, **Then** the desired-state revision contains a valid observability binding record.

---

### User Story 3 - Support Contextual Validation Fixes (Priority: P3)

A deployment operator follows a validation action that asks them to add a record needed by a higher target environment and sees the relevant editor even if the current source environment does not require it.

**Why this priority**: Promotion blockers should guide users directly to the missing record without permanently showing production-only controls on every source revision form.

**Independent Test**: Can be fully tested by opening a Dev revision page with a contextual request to add observability and verifying the editor appears with language explaining the target validation reason.

**Acceptance Scenarios**:

1. **Given** a promotion validation action links to a Dev new revision page with an observability fix request, **When** the page opens, **Then** the observability editor is visible and explains it is included to satisfy validation outside the current Dev tier.
2. **Given** the same page is opened without the fix request, **When** the current environment tier does not require observability, **Then** the editor is hidden.

### Edge Cases

- The environment has no tier capabilities because data is unavailable; the page should not invent requirements and should state that requirement metadata is unavailable.
- A tier capability is unknown to the current UI; the page should show a generic requirement row only if backend metadata supplies a label and description, and otherwise ignore it safely.
- A user lacks desired-state management permission; requirement visibility remains read-only context and submission remains blocked.
- A validation action requests a record kind that is not supported by the current form; the page should report the unsupported contextual request instead of rendering incorrect fields.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST derive desired-state requirement visibility from the selected environment's tier capabilities rather than hardcoded tier names or global production text.
- **FR-002**: System MUST hide the observability binding editor on new revision pages when the current environment tier does not require observability and no contextual fix request is present.
- **FR-003**: System MUST show an observability binding editor on new revision pages when the current environment tier includes the observability-required capability.
- **FR-004**: System MUST mark tier-required desired-state record editors as required and validate their required fields before revision submission.
- **FR-005**: System MUST allow a contextual validation fix request to show a supported record editor even when the current environment tier does not require that record.
- **FR-006**: System MUST explain whether a displayed desired-state requirement is required by the current environment tier or included to satisfy a contextual validation fix.
- **FR-007**: System MUST submit only records that are required, contextually requested, or explicitly enabled by the user.
- **FR-008**: System MUST keep backend validation and frontend requirement metadata aligned through shared stable identifiers for tier capabilities, record kinds, and validation IDs.
- **FR-009**: System MUST avoid exposing raw secrets or credential values in any desired-state requirement metadata or records.
- **FR-010**: System MUST preserve existing revision creation behavior for artifact records, revision labels, commits, permissions, and navigation after successful creation.

### Key Entities *(include if feature involves data)*

- **Desired-State Requirement**: A platform-defined rule that describes a desired-state record kind needed or suggested for a tier capability or contextual validation fix.
- **Requirement Applicability**: The reason a requirement is displayed, such as current-tier required, contextual fix, optional advanced record, or unavailable metadata.
- **Observability Binding Record**: Desired-state record describing the telemetry signal, provider, scope, and note used to satisfy observability validation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Dev and Test new revision pages no longer show production-only observability controls by default.
- **SC-002**: Production-like new revision pages show required observability controls without users consulting documentation.
- **SC-003**: A user can create a Dev revision without interacting with observability controls in the same number of steps as before or fewer.
- **SC-004**: A user following an observability validation action reaches a pre-opened editor that explains why the record is being added.
- **SC-005**: Automated tests prove frontend visibility and backend requirement metadata remain aligned for tiers with and without `deployment.observability.required`.

## Assumptions

- Tier capabilities remain the authoritative source of environment semantics.
- `deployment.observability.required` continues to mean at least one `ObservabilityBinding` record is required by validation.
- The initial implementation supports observability requirements and leaves room for future desired-state record types.
- Contextual fix requests can be represented through URL query parameters for this iteration.
