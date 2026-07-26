# Feature Specification: Runtime Image Metadata API

**Feature Branch**: `010-runtime-image-metadata-api`

**Created**: 2026-05-18

**Status**: Draft

**Input**: User description: "Move deployment-affecting Docker runtime image metadata out of the Lovable static frontend file and into the Valence Control backend so image selection, environment defaults, companion rules, and bundle generation use a single source of truth."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select Runtime Images From Valence Control Metadata (Priority: P1)

A Runtime Builder user can choose from Elsa runtime images supplied by the platform rather than relying on a frontend-only static image list.

**Why this priority**: Bundle generation cannot be authoritative while Docker image identity, defaults, ports, environment variables, and companion rules exist only in the browser.

**Independent Test**: Can be tested by requesting builder catalog metadata and verifying the expected image choices and deployment-affecting fields are available to populate the image selector.

**Acceptance Scenarios**:

1. **Given** platform runtime image metadata exists, **When** the builder catalog is loaded, **Then** image choices include the known server, Studio, and combined runtime images.
2. **Given** an image is selected, **When** the frontend renders image configuration, **Then** it can display deployment-relevant defaults from platform metadata.
3. **Given** platform image metadata is temporarily unavailable during migration, **When** Lovable loads the builder, **Then** the frontend can still use its temporary fallback until the rollout is complete.

---

### User Story 2 - Use Image Metadata During Bundle Generation (Priority: P1)

A generated deployment bundle uses platform-owned image metadata for image references, default tags, ports, container names, environment variables, and companion behavior.

**Why this priority**: The backend bundle generator must not copy or infer deployment behavior from frontend-only files.

**Independent Test**: Can be tested by generating bundles for each supported image and verifying that image references, ports, environment placeholders, and companion behavior match platform metadata.

**Acceptance Scenarios**:

1. **Given** a selected image has default ports and environment variables, **When** a bundle is generated, **Then** those defaults appear consistently in generated deployment files.
2. **Given** a selected image requires a companion server image, **When** a bundle is generated, **Then** the companion runtime behavior is included according to platform metadata.
3. **Given** an unknown image slug is submitted, **When** bundle generation or planning is requested, **Then** the request is rejected with a clear finding.

---

### User Story 3 - Separate Deployment Metadata From Presentation Copy (Priority: P2)

Product contributors can distinguish deployment-affecting image metadata from marketing or documentation-only presentation fields.

**Why this priority**: Some existing frontend image fields are visual or documentation concerns. Moving only deployment truth first keeps ownership clear and avoids unnecessary backend coupling to frontend design.

**Independent Test**: Can be tested by reviewing migrated image fields and verifying that every deployment-affecting field is platform-owned while purely presentational fields are either optional or explicitly classified.

**Acceptance Scenarios**:

1. **Given** the existing image data is exported from Lovable, **When** fields are classified, **Then** deployment-affecting fields are required in platform metadata.
2. **Given** a field is only used for visual styling or static marketing pages, **When** metadata is reviewed, **Then** it is marked optional or frontend-owned.
3. **Given** docs pages still depend on frontend-only fields, **When** platform image metadata rolls out, **Then** docs pages continue to work during the migration.

---

### User Story 4 - Validate Runtime Image Catalog Quality (Priority: P3)

Operators and contributors can detect incomplete or inconsistent runtime image definitions before they affect builder users.

**Why this priority**: Image metadata drives generated deployment output, so invalid definitions can create broken bundles.

**Independent Test**: Can be tested by validating seeded image metadata and confirming invalid slugs, missing image references, duplicate environment variables, or broken companion references are reported.

**Acceptance Scenarios**:

1. **Given** image metadata contains duplicate environment variable names for one image, **When** validation runs, **Then** the duplicate is reported.
2. **Given** an image references a companion image slug that does not exist, **When** validation runs, **Then** the broken reference is reported.
3. **Given** every required image field is present and consistent, **When** validation runs, **Then** all known runtime images pass.

---

### User Story 5 - Configure Runtime Images From Backend Definitions (Priority: P2)

Valence Control operators can curate the runtime images and image-level configurable attributes exposed by Runtime Builder without changing console code.

**Why this priority**: The builder UI should be a generic renderer of platform image metadata. Adding an image, changing exposed image attributes, or adjusting deployment-shape defaults should not require editing React select options or per-image form logic.

**Independent Test**: Can be tested by changing a backend-owned image definition and verifying the builder catalog response and runtime image form reflect the change without frontend code changes.

**Acceptance Scenarios**:

1. **Given** an operator adds or updates a runtime image in the backend-owned catalog source, **When** the builder catalog is loaded, **Then** the image selector reflects the configured image definition.
2. **Given** a runtime image exposes environment variables or other configurable attributes, **When** the frontend renders the runtime image step, **Then** it renders those attributes from backend metadata rather than hardcoded image-specific UI.
3. **Given** an image or image attribute is disabled, deprecated, renamed, or removed, **When** saved runtime configurations reference it, **Then** the system reports a clear validation finding and preserves enough metadata for the user to repair the configuration.
4. **Given** an invalid image definition is configured, **When** the platform starts or the catalog source is loaded, **Then** validation prevents the invalid definition from becoming authoritative for bundle generation.

### Edge Cases

- The frontend asks for an image slug that no longer exists.
- An image has no available tags or a default tag that is not in the available tag set.
- Two images define the same slug.
- An image reference is empty, malformed, or points to the wrong runtime family.
- Environment variable names are duplicated or use unsupported characters.
- A required environment variable has no default and must be represented as a user-supplied placeholder.
- Studio requires a companion server image, but the companion image is missing or incompatible.
- A deployment-affecting field is accidentally kept only in frontend fallback data.
- Marketing copy changes without changing deployment behavior.
- A configured image is hidden for new configurations but still referenced by saved configurations.
- A configurable image attribute is removed or renamed while saved configurations still contain overrides.
- A configured environment variable is marked secret and must not expose an unsafe default value.
- Runtime image definitions differ by organization, workspace, license entitlement, or deployment target.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose runtime image metadata for builder clients.
- **FR-002**: Runtime image metadata MUST include stable slug, display name, description, Docker image reference, available tags, default tag, default port, default host port, suggested container name, license tier, stability, capabilities, environment variable definitions, deployment hints, and documentation links when available.
- **FR-003**: System MUST include the known initial runtime image choices: professional server, professional Studio, and professional combined runtime.
- **FR-004**: System MUST identify which metadata fields affect deployment behavior and which fields are presentation or documentation only.
- **FR-005**: System MUST use platform-owned image metadata when generating deployment bundles.
- **FR-006**: System MUST represent runtime image environment variables with name, display name, description, required flag, secret flag, default value, group, and advanced flag when available.
- **FR-007**: System MUST represent image deployment hints such as supported deployment formats, companion image requirements, shared network needs, and image capabilities.
- **FR-008**: System MUST reject unknown image slugs in builder, planning, and bundle-generation flows with a clear finding or validation result.
- **FR-009**: System MUST validate image metadata for unique slugs, usable image references, valid default tags, unique environment variable names, and valid companion image references.
- **FR-010**: System MUST support a migration period where Lovable can use platform metadata when present and a local fallback when absent.
- **FR-011**: Deployment-affecting image defaults MUST NOT remain authoritative only in Lovable after migration is complete.
- **FR-012**: System SHOULD allow image metadata to be curated without requiring frontend redeployment for deployment-shape changes.
- **FR-013**: Documentation or marketing fields MAY remain frontend-owned when they do not affect generated deployment output.
- **FR-014**: The backend runtime image catalog MUST be the source of truth for available runtime images and image-level configurable attributes exposed by Runtime Builder.
- **FR-015**: Runtime Builder frontend code MUST render image choices and image-level configurable attributes generically from catalog metadata, without hardcoded per-image option lists or deployment-affecting defaults.
- **FR-016**: Runtime image catalog loading MUST validate configured definitions before they are used by planning or bundle generation.
- **FR-017**: System MUST define lifecycle behavior for hidden, disabled, deprecated, or removed images and attributes so saved configurations remain diagnosable and repairable.

### Key Entities *(include if feature involves data)*

- **Runtime Image**: A selectable Elsa Docker runtime image with stable slug, display metadata, image reference, tags, ports, license and stability classification, capabilities, environment variables, and deployment hints.
- **Runtime Image Environment Variable**: A configurable environment variable definition for a runtime image, including secret and required markers.
- **Runtime Image Deployment Hint**: Metadata that controls how an image participates in generated deployment output, including supported targets and companion image behavior.
- **Runtime Image Docs**: Optional documentation metadata such as Docker Hub links or container path references.
- **Frontend Fallback Image Data**: Temporary Lovable-owned image metadata used only during migration when platform metadata is unavailable.
- **Runtime Image Catalog Source**: Backend-owned source for runtime image definitions. It may start as source-controlled or appsettings-backed metadata, and may later become persisted/admin-managed once product ownership and scoping are defined.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Builder catalog consumers can render all three known runtime images from platform-provided metadata.
- **SC-002**: Bundle generation for each known runtime image uses platform-owned image reference, tag, port, container name, environment variables, and companion behavior.
- **SC-003**: Runtime image metadata validation detects duplicate slugs, missing image references, invalid default tags, duplicate environment variables, and broken companion references.
- **SC-004**: During migration, Lovable can load image metadata from the platform and still fall back locally when platform metadata is unavailable.
- **SC-005**: After migration, no deployment-affecting image metadata is authoritative only in the frontend static image file.
- **SC-006**: Presentation-only metadata can change without altering generated deployment output.
- **SC-007**: A backend image definition change appears in the builder catalog and runtime image form without editing console source code.
- **SC-008**: Invalid configured image definitions are rejected before they can affect generated deployment bundles.

## Assumptions

- Runtime image metadata may start as source-controlled or configured seed data before any admin-managed database workflow exists.
- The first migration imports the existing server, Studio, and combined professional image definitions from Lovable.
- The first frontend migration keeps local fallback data to reduce rollout risk.
- Full runtime image tag discovery can be curated manually at first; automated registry discovery is not required for this feature.
- Runtime image metadata is required before bundle generation can fully remove frontend deployment defaults.
- Current code may use static backend seed metadata as an intermediate step, but that is not the long-term operator-configurable catalog source.

## Clarifications Needed

- Should runtime image definitions be global platform settings, organization-scoped, workspace-scoped, or filtered by entitlement while remaining globally authored?
- Should the first configurable source be appsettings/source-controlled metadata, an operator API, or persisted admin-managed records?
- Should image tags remain manually curated, or should the platform discover tags from a registry and allow operators to approve them?
- What lifecycle states are required for images and attributes: active, hidden, deprecated, disabled, removed, or superseded?
