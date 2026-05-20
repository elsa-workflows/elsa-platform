# Feature Specification: Deployment Manifest Parsing

**Feature Branch**: `018-deployment-manifest`

**Created**: 2026-05-20

**Status**: Draft

**Input**: User description: "Implement the next Phase 1 deployment slice: v1alpha deployment manifest parsing, schema validation, and normalization for Elsa Deployment Platform."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Parse A Versioned Environment Manifest (Priority: P1)

A platform maintainer can load a v1alpha environment manifest and convert its workflow, variable, feature, package, and recipe entries into normalized deployment resources that use the foundation contracts from `Elsa.Platform.Deployment.Abstractions`.

**Why this priority**: The manifest is the first input to the deployment loop. Artifact, validation, dry-run, apply, CLI, and API work cannot proceed reliably until the manifest shape is parsed into the shared deployment resource model.

**Independent Test**: Can be tested by loading a representative manifest and asserting normalized resources, identity, scope, dependencies, deletion behavior, metadata, and diagnostics without requiring artifact IO or a deployment engine.

**Acceptance Scenarios**:

1. **Given** a valid v1alpha manifest containing workflows and variables, **When** it is parsed, **Then** normalized deployment resources are produced with stable resource types and logical ids.
2. **Given** a valid manifest containing feature, package, and recipe descriptors, **When** it is parsed, **Then** descriptor resources are produced without implying apply support.
3. **Given** optional metadata and dependencies, **When** a manifest is normalized, **Then** safe metadata and resource dependencies are preserved.

---

### User Story 2 - Report Manifest Diagnostics (Priority: P2)

A contributor or CI job can validate malformed or unsupported manifests and receive structured diagnostics instead of exceptions or ambiguous parser output.

**Why this priority**: The deployment loop must distinguish schema, parse, resource, and unsupported-version failures before it can produce trustworthy dry-run and apply behavior.

**Independent Test**: Can be tested by loading invalid manifests and asserting diagnostic codes, severity, messages, and optional resource association.

**Acceptance Scenarios**:

1. **Given** a manifest with an unsupported `apiVersion`, **When** it is validated, **Then** validation fails with a structured unsupported-version diagnostic.
2. **Given** a manifest with missing required resource identity fields, **When** it is validated, **Then** validation fails with resource-specific diagnostics.
3. **Given** syntactically invalid YAML or JSON, **When** it is parsed, **Then** parsing fails with a diagnostic and does not produce normalized resources.

---

### User Story 3 - Preserve Forward-Compatible Manifest Shape (Priority: P3)

A future deployment package can add new metadata and resource handler bindings without changing the Phase 1 manifest contract, while unknown resource types remain rejected unless a handler mapping is registered.

**Why this priority**: The v1alpha manifest should be narrow but not throwaway. This slice must protect future resource extensions without adding overlays, policy engines, OCI, operators, or runtime adapters.

**Independent Test**: Can be tested by parsing manifests with extension metadata, unknown resource types, and registered custom type mappings.

**Acceptance Scenarios**:

1. **Given** safe extension metadata, **When** a manifest is parsed, **Then** the metadata is preserved in normalized output.
2. **Given** an unknown resource section without a registered mapping, **When** validation runs, **Then** the manifest is rejected with an unsupported-resource diagnostic.
3. **Given** a registered custom resource mapping, **When** a manifest includes that resource section, **Then** normalized resources use the registered deployment resource type.

### Edge Cases

- Missing or empty `apiVersion`, `kind`, `metadata.name`, or resource identity fields.
- Unsupported `kind` values.
- Duplicate resource identities after normalization.
- Relative paths that escape the manifest root.
- Unknown resource sections without a registered mapper.
- Descriptor resources that are valid for validation but unsupported for apply.
- Empty manifests with no resources.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define a `Elsa.Platform.Deployment.Manifest` package that depends on `Elsa.Platform.Deployment.Abstractions` and not on engine, CLI, API, Package Catalog implementation, Runtime Builder implementation, persistence, or hosting packages.
- **FR-002**: The system MUST parse v1alpha environment manifests from YAML and JSON text.
- **FR-003**: The system MUST require `apiVersion: platform.elsa.io/v1alpha1`, `kind: EnvironmentManifest`, and `metadata.name`.
- **FR-004**: The system MUST normalize workflow, variable, feature, package, and recipe entries into `DeploymentResource` instances with stable resource types.
- **FR-005**: The system MUST preserve safe manifest metadata and resource metadata as string dictionaries.
- **FR-006**: The system MUST compute deterministic desired-state hashes for normalized resources.
- **FR-007**: The system MUST produce structured `DeploymentDiagnostic` entries for parse errors, schema errors, unsupported versions, duplicate resource identities, invalid paths, and unsupported resource types.
- **FR-008**: The system MUST reject unknown resource sections unless a resource mapper is registered for the section.
- **FR-009**: The system MUST expose manifest reader and normalizer contracts that later artifact, engine, CLI, and API packages can consume.
- **FR-010**: The system MUST include focused tests for valid manifests, invalid manifests, normalization, diagnostics, extension metadata, unknown resource sections, and boundary references.

### Key Entities *(include if feature involves data)*

- **Deployment Manifest**: Versioned desired-state document with metadata and resource sections.
- **Manifest Metadata**: Name, optional version, optional environment, labels, annotations, and source metadata.
- **Manifest Resource Entry**: Raw workflow, variable, feature, package, recipe, or custom resource entry before normalization.
- **Normalized Manifest**: Parsed manifest plus ordered deployment resources and diagnostics.
- **Manifest Diagnostic**: Structured parse, schema, or normalization message.
- **Resource Mapper**: Extension point that converts a manifest resource section into deployment resources.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A representative v1alpha manifest with workflows and variables normalizes into deterministic deployment resources through automated tests.
- **SC-002**: Invalid manifest tests cover unsupported version, unsupported kind, duplicate identities, missing required fields, invalid paths, and malformed YAML/JSON.
- **SC-003**: YAML and JSON manifests with equivalent content produce equivalent normalized resources and desired-state hashes.
- **SC-004**: Boundary tests verify the manifest package references abstractions but not engine, CLI, API, catalog implementation, runtime builder implementation, hosting, persistence, or UI packages.
- **SC-005**: Documentation identifies the exact manifest shape supported in this slice and deferred features such as overlays, secrets, artifact IO, engine execution, CLI, API, OCI, signing, and policy evaluation.

## Assumptions

- YAML and JSON parsing may use repository-approved .NET libraries, but the manifest package remains independent from hosting, persistence, and engine packages.
- This slice does not implement folder/ZIP artifacts, deployment planning, dry-run, apply, CLI, API, runtime adapters, overlays, secret references, signatures, OCI, GitOps, Kubernetes, or policy evaluation.
- Workflow and variable resources are normalized as deployable resources; feature, package, and recipe entries are normalized as descriptor resources only.
- Paths are parsed and validated as manifest-relative logical paths; file content loading belongs to artifact IO slices.
