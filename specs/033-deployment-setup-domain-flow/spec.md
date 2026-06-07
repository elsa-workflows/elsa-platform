# Feature Specification: Deployment Setup Domain Flow

**Feature Branch**: `033-deployment-setup-domain-flow`

**Created**: 2026-06-07

**Status**: Draft

**Input**: User description: "Create a spec then implement a deployment setup UX that models the domain clearly: creating an environment should not also create an engine; engines should be registered within an environment; engine base URL/name/credential fields should be understandable; credential provider and reference should be selected from configurable secret stores/references registered with the system rather than guessed free text."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create Environment Without Engine Coupling (Priority: P1)

A deployment administrator creates a new environment for an application by entering only the environment details that belong to the environment itself.

**Why this priority**: The current combined form makes users configure runtime endpoint and credential details before they have finished defining the environment. Separating the concepts makes the domain model understandable and lets planned environments exist before runtime infrastructure is ready.

**Independent Test**: Can be tested by creating an environment from an application and verifying the resulting environment exists with no engine registration requirement.

**Acceptance Scenarios**:

1. **Given** an application and at least one active deployment tier, **When** an authorized user creates an environment, **Then** the form asks only for application, environment name, and tier.
2. **Given** the environment is created successfully, **When** the user lands on the environment page, **Then** the environment shows an engine-registration empty state and action rather than pretending an engine already exists.
3. **Given** an unauthorized user opens the create-environment flow, **When** they attempt setup work, **Then** setup mutation remains unavailable according to deployment setup permission.

---

### User Story 2 - Register Engines Inside An Environment (Priority: P1)

A deployment administrator registers one or more Elsa workflow engine endpoints from within the environment they belong to.

**Why this priority**: Engine registrations are children of an environment. Registering them inside the environment keeps setup steps understandable while preserving a fast next-step path after environment creation.

**Independent Test**: Can be tested by opening an environment with no engines, using the register-engine action, entering engine details, and verifying the engine appears in the environment's registration list with health/credential status.

**Acceptance Scenarios**:

1. **Given** an environment has no engines, **When** an authorized user opens the environment, **Then** the engine registrations section explains that no engines are registered and offers a Register engine action.
2. **Given** an authorized user registers an engine, **When** they submit valid endpoint and credential reference details, **Then** the engine is created for that environment and verification starts through the existing engine health flow.
3. **Given** an engine is registered, **When** the user reviews environment details, **Then** the UI uses clear labels for Engine name and Engine base URL.

---

### User Story 3 - Choose Registered Secret References (Priority: P2)

A deployment administrator selects engine credentials from registered workspace secret stores and references instead of memorizing provider names and reference strings.

**Why this priority**: Credential provider and reference free-text inputs make the system hard to use and easy to misconfigure. A registry gives users a discoverable, governed list while still avoiding raw secret storage.

**Independent Test**: Can be tested by registering a secret store and reference, then registering an engine and verifying the credential store and reference are selected from available options.

**Acceptance Scenarios**:

1. **Given** a workspace has active secret stores, **When** a user registers an engine, **Then** the credential store is selected from active stores instead of typed as arbitrary provider text.
2. **Given** a selected secret store has active credential references, **When** a user chooses an engine credential, **Then** the reference picker only shows references for that store.
3. **Given** no active credential references exist for the selected store, **When** the user opens the engine form, **Then** the form clearly shows that a credential reference must be registered first.
4. **Given** existing engines use legacy free-text provider/reference data, **When** the user opens deployment cockpit data, **Then** those values remain readable and usable during transition.

---

### User Story 4 - Manage Secret Store Metadata (Priority: P2)

A deployment administrator manages workspace-scoped secret store and credential reference metadata without storing secret values in Elsa Platform.

**Why this priority**: Pickers require an authoritative source. The registry must be manageable in the deployment setup surface and must preserve the safety boundary that raw secrets live outside the platform.

**Independent Test**: Can be tested by creating, listing, updating, and archiving secret-store metadata and credential-reference metadata, then verifying archived items are not offered for new engine registration.

**Acceptance Scenarios**:

1. **Given** an authorized user opens deployment setup, **When** they create a secret store, **Then** the system stores only provider/type metadata and no raw secret values.
2. **Given** an authorized user creates a credential reference for a store, **When** the reference is listed, **Then** the response includes safe label/reference metadata and status, but no credential value.
3. **Given** a secret store or credential reference is archived, **When** a new engine is registered, **Then** archived items are not selectable for new registrations while existing engines remain readable.

### Edge Cases

- A workspace has no active tiers when creating an environment.
- A workspace has no registered secret stores when registering an engine.
- A selected secret store has no active credential references.
- A credential reference is archived after an engine already uses it.
- An environment has multiple engines.
- An engine base URL is not an absolute URL.
- Existing legacy engine credentials have provider/reference strings that do not match a registered store/reference.
- A user has deployment read permission but not deployment setup permission.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow authorized users to create deployment environments using only application, environment name, and tier inputs.
- **FR-002**: System MUST NOT require or create a workflow engine registration as part of environment creation.
- **FR-003**: System MUST present engine registration as an environment-scoped action after an environment exists.
- **FR-004**: System MUST allow authorized users to register workflow engines for an existing environment with engine name, engine base URL, credential reference, capabilities, controls, and optional hosting metadata.
- **FR-005**: System MUST label engine fields in user-facing UI so the difference between engine name and engine base URL is clear.
- **FR-006**: System MUST store workspace-scoped secret store metadata without storing raw secret values.
- **FR-007**: System MUST store workspace-scoped credential reference metadata for a selected secret store without storing raw credential values.
- **FR-008**: System MUST provide authorized users with active secret stores and active credential references as selectable options during engine registration.
- **FR-009**: System MUST scope credential-reference choices to the selected secret store.
- **FR-010**: System MUST prevent archived secret stores and archived credential references from being used for new engine registrations.
- **FR-011**: System MUST keep existing engine credential provider/reference values readable and usable even when they do not yet map to registered secret-store metadata.
- **FR-012**: System MUST continue to enforce existing deployment setup permissions for environment, engine, secret-store, and credential-reference mutations.
- **FR-013**: System MUST never expose raw engine credentials, provider tokens, or secret values in API responses, console state, desired-state revisions, or audit records.
- **FR-014**: System MUST preserve current engine health verification behavior after engine registration.

### Key Entities *(include if feature involves data)*

- **Deployment Environment**: Application-scoped deployment lane with a name, tier, and zero or more engine registrations.
- **Workflow Engine Registration**: Environment-scoped runtime endpoint with display name, base URL, health metadata, credential reference metadata, capability set, and controls.
- **Secret Store**: Workspace-scoped metadata record describing an external credential provider or store that can host engine credentials.
- **Credential Reference**: Workspace-scoped metadata record under a secret store that points to an externally managed engine credential without containing the credential value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can create a new environment without entering engine endpoint or credential details.
- **SC-002**: A user can register an engine from the environment page after the environment exists.
- **SC-003**: A user can complete engine registration using selectable credential-store/reference options when registered options exist.
- **SC-004**: New setup users do not need to know provider-string or credential-reference formats to discover valid registered options.
- **SC-005**: API and console responses for secret stores, credential references, and engine registrations expose no raw secret values.
- **SC-006**: Existing deployment cockpit data with legacy credential provider/reference strings remains visible after the change.

## Assumptions

- Workspace deployment setup permission remains the governing permission for environment, engine, secret-store, and credential-reference mutations.
- Secret-store and credential-reference records are safe metadata only; integrating with real provider APIs for browsing vault contents is out of scope for this feature.
- Credential-reference values may still be opaque provider-backed paths or identifiers, but users choose them from registered metadata rather than typing them during engine registration.
- Existing legacy engine rows retain provider/reference string fields for compatibility while new engine registration can derive those strings from selected registry records.
- Engine capabilities and controls remain the existing default values in the console until a separate feature adds discovery or provider-specific capability negotiation.
