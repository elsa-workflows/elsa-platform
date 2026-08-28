# Feature Specification: Engine Credential Secret Stores

**Feature Branch**: `035-engine-secret-stores`

**Created**: 2026-06-08

**Status**: Draft

**Input**: User description: "Secret stores in this Elsa Control are only for engine credentials. Runtime secrets are managed separately from within the runtimes themselves; deployment artifacts may include secret references, but those are unrelated to platform engine credentials. Engine credentials are used purely so Elsa Control can interact with a registered engine, such as notifying it that a new manifest has been provisioned. Supported store types should include local encrypted database storage, Azure Key Vault, Kubernetes Secrets, environment variable name, and generic external reference. Secret stores are workspace-scoped. Engine credentials may be deferred."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create Engine Credential Store (Priority: P1)

A deployment administrator registers a workspace-scoped credential store that can hold or identify credentials used by Elsa Control to interact with workflow engines.

**Why this priority**: Engine registration currently depends on credential choices, but users can reach that point with no clear way to prepare credentials. A workspace-level store registry gives administrators a governed setup path without mixing engine credentials with runtime secrets.

**Independent Test**: Can be tested by creating a store for each supported store type and verifying it appears as an active workspace option for engine credential setup without exposing unrelated runtime-secret concepts.

**Acceptance Scenarios**:

1. **Given** a workspace administrator opens credential setup, **When** they create a local encrypted credential store, **Then** the store is registered as a workspace engine-credential store and can contain engine credential entries.
2. **Given** a workspace administrator opens credential setup, **When** they create an Azure Key Vault, Kubernetes Secrets, environment variable name, or generic external reference store, **Then** the store is registered as a workspace engine-credential store that points to externally managed credential material.
3. **Given** any credential store is listed, **When** a user views it, **Then** the UI makes clear that it is for platform-to-engine credentials only and not for runtime secrets or artifact secret references.

---

### User Story 2 - Add Engine Credential References (Priority: P1)

A deployment administrator adds one or more credential references under a store so engines can reuse understandable credential entries instead of requiring free-text provider and reference fields.

**Why this priority**: Engine credential setup should be discoverable and repeatable. Named references let administrators configure credentials once and attach them to one or more engine registrations.

**Independent Test**: Can be tested by adding credential references to each store type, selecting them during engine setup, and verifying no secret value is shown after creation.

**Acceptance Scenarios**:

1. **Given** an active credential store, **When** an administrator creates a credential reference, **Then** the reference has a human-readable name, store-specific locator or value metadata, status, and optional description.
2. **Given** the store type is local encrypted database, **When** an administrator creates a credential reference, **Then** they can provide the engine credential secret value at creation or rotation time and the value is never shown again.
3. **Given** the store type is externally managed, **When** an administrator creates a credential reference, **Then** they provide only the external locator needed to resolve the engine credential and no raw secret value is collected.
4. **Given** a credential reference has been created, **When** it is shown in lists or engine forms, **Then** users see only safe metadata such as name, store, locator, status, and last verification information.

---

### User Story 3 - Register Engines With Optional Credentials (Priority: P1)

A deployment administrator registers an engine with a selected credential reference when one is ready, or defers credential assignment when runtime infrastructure exists before access credentials are available.

**Why this priority**: Deferring credentials lets teams model environments and engines early without implying that a single environment or engine setup must be fully credentialed immediately.

**Independent Test**: Can be tested by registering an engine with no credential reference, confirming the engine is marked as needing credentials, then assigning a reference later and confirming the engine becomes eligible for credentialed platform interactions.

**Acceptance Scenarios**:

1. **Given** an environment is ready for engine registration, **When** no credential store or reference exists, **Then** the user can still register the engine with credentials deferred.
2. **Given** credentials are deferred, **When** the engine is displayed, **Then** its credential status clearly indicates that platform-to-engine commands cannot be sent until credentials are assigned.
3. **Given** active credential references exist, **When** an administrator registers or edits an engine, **Then** they can select a reference scoped to the workspace.
4. **Given** an engine was registered with credentials deferred, **When** an administrator assigns an active credential reference later, **Then** the engine uses that reference for future platform-to-engine interactions.

---

### User Story 4 - Maintain Credential Store Lifecycle (Priority: P2)

A deployment administrator manages credential stores and references across their lifecycle without breaking existing engine registrations unexpectedly.

**Why this priority**: Credential metadata changes over time. Administrators need a safe way to rotate, verify, archive, and understand credential usage.

**Independent Test**: Can be tested by rotating a local credential, changing an external reference locator, archiving unused references, and verifying in-use references are clearly identified before disruptive changes.

**Acceptance Scenarios**:

1. **Given** a credential reference is in use by one or more engines, **When** an administrator views or edits it, **Then** the system shows that usage before allowing changes that may affect engine communication.
2. **Given** a local encrypted credential needs rotation, **When** an administrator updates the value, **Then** the old value is replaced without exposing either value in lists, details, logs, or history.
3. **Given** an externally managed credential reference changes, **When** an administrator updates its locator metadata, **Then** affected engine registrations continue to point to the same named reference.
4. **Given** a credential store or reference is archived, **When** a user registers or edits an engine, **Then** archived items are not offered for new assignment while existing engines remain understandable.

### Edge Cases

- A workspace has no credential stores when a user reaches engine setup.
- A workspace has credential stores but no active credential references.
- A credential reference is archived while an engine still uses it.
- A local encrypted credential is rotated by a user who lacks permission to view or change credential material.
- An external provider locator is malformed, unreachable, or not verifiable.
- An environment variable name exists only in engine host infrastructure and cannot be verified from Elsa Control.
- A Kubernetes secret name exists in multiple namespaces or contexts.
- A generic external reference is meaningful to the customer but cannot be validated by Elsa Control.
- A user confuses engine credentials with runtime secrets or artifact secret references.
- A workspace has multiple applications and environments sharing the same credential reference.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST treat secret stores in this feature as engine-credential stores only, used for Elsa Control interactions with registered engines.
- **FR-002**: System MUST keep runtime secrets and artifact secret references outside the scope of engine credential stores.
- **FR-003**: System MUST scope engine credential stores and credential references to a workspace.
- **FR-004**: System MUST allow authorized administrators to create, view, update, and archive workspace engine credential stores.
- **FR-005**: System MUST support these engine credential store types: local encrypted database, Azure Key Vault, Kubernetes Secrets, environment variable name, and generic external reference.
- **FR-006**: System MUST define generic external reference as a metadata-only pointer to a customer-governed credential location that Elsa Control does not natively resolve, browse, or verify, such as an internal secret catalog entry, vault URI, ticket-controlled secret record, or provider type not yet first-class.
- **FR-007**: System MUST allow authorized administrators to create, view, update, archive, and rotate credential references under active credential stores.
- **FR-008**: System MUST allow local encrypted database references to accept credential secret material during creation and rotation while preventing that material from being displayed after submission.
- **FR-009**: System MUST require externally managed store references to capture only safe locator metadata and MUST NOT collect raw secret values for those store types.
- **FR-010**: System MUST allow engine registration with credentials deferred.
- **FR-011**: System MUST clearly mark engines with deferred credentials as unable to receive credentialed platform-to-engine commands until a credential reference is assigned.
- **FR-012**: System MUST allow authorized administrators to assign or change an engine credential reference after engine registration.
- **FR-013**: System MUST offer only active workspace credential references for new engine assignment.
- **FR-014**: System MUST keep existing engine assignments readable when their referenced store or credential reference is later archived.
- **FR-015**: System MUST show where each credential reference is used before administrators perform lifecycle actions that may affect existing engine communication.
- **FR-016**: System MUST provide verification state for credential references where verification is possible, while allowing store types that cannot be verified to remain explicitly unverified.
- **FR-017**: System MUST prevent secret values, provider tokens, decrypted credentials, and sensitive credential material from appearing in normal UI, logs, histories, audit records, deployment artifacts, or command records.
- **FR-018**: System MUST make the distinction between engine credentials, runtime secrets, and artifact secret references visible in credential setup surfaces.
- **FR-019**: System MUST enforce existing workspace deployment setup permissions for credential store, credential reference, and engine credential assignment changes.
- **FR-020**: System MUST allow multiple engines across a workspace to use the same active credential reference.

### Key Entities *(include if feature involves data)*

- **Engine Credential Store**: Workspace-scoped credential container or locator category used only for platform-to-engine credentials. It has a name, store type, status, description, and safe provider metadata.
- **Engine Credential Reference**: Named credential entry under an engine credential store. It represents either locally protected credential material or an external locator that can be assigned to engines.
- **Engine Credential Assignment**: The relationship between a registered engine and an engine credential reference, or an explicit deferred-credentials state.
- **Credential Verification State**: The current confidence signal for a credential reference, such as verified, unverified, missing, invalid, or not verifiable.
- **Credential Usage**: The set of engines currently depending on a credential reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Administrators can create a workspace credential store and credential reference for each supported store type in under 5 minutes per type during acceptance testing.
- **SC-002**: Administrators can register an engine with credentials deferred and later assign credentials without recreating the environment or engine.
- **SC-003**: 100% of engine credential setup screens avoid collecting runtime secret values or artifact secret-reference data.
- **SC-004**: 100% of credential lists and detail views hide raw secret values and display only safe metadata after credential submission.
- **SC-005**: Administrators can identify all engines using a credential reference before archiving or changing it.
- **SC-006**: New users can correctly explain whether a credential setup field is for engine credentials or runtime secrets after reading the screen labels in moderated review.
- **SC-007**: Existing engines remain visible and understandable when their credential reference is archived or unverified.

## Assumptions

- Workspace deployment setup permission remains the governing permission for credential store, credential reference, and engine credential assignment changes.
- Local encrypted database storage is intended for engine credentials only and does not become a general-purpose runtime secret store.
- Externally managed providers are responsible for protecting and rotating their own secret material.
- Elsa Control may verify some providers directly, but generic external references and environment variable names may remain not verifiable by design.
- Deferred credentials mean engine metadata can exist, but credentialed platform-to-engine actions are blocked or marked unavailable until a credential reference is assigned.
- Engine credentials are used for platform commands such as notifying an engine that a manifest has been provisioned.
- Runtime secret management remains inside the runtimes, and deployment artifacts may continue to carry secret references that are unrelated to this feature.
