# Feature Specification: Platform Package Catalog Consolidation

**Feature Branch**: `001-platform-package-catalog-consolidation`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Reimagine the current `elsa-package-catalog` repository as an Elsa Platform subsystem, move toward the ideal package layout under `elsa-platform`, deprecate the old repository, improve architecture where useful, and use Spec Kit for implementation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish Platform Home (Priority: P1)

Elsa maintainers can see the package catalog, package manifests, and package manifest generator represented as first-class Elsa Platform subsystems with clear ownership, naming, boundaries, and migration phases.

**Why this priority**: Before code moves, the team needs a shared target architecture and execution sequence that prevents catalog concerns from being buried inside deployment or drifting from platform direction.

**Independent Test**: Review the spec, plan, contracts, and tasks and verify that they define the target repository structure, migration phases, progress tracking model, and old-repository deprecation path without requiring implementation code.

**Acceptance Scenarios**:

1. **Given** the new platform repo is being used for implementation planning, **When** a maintainer opens the Spec Kit feature, **Then** the plan identifies Package Catalog as a sibling subsystem to Deployment.
2. **Given** the current catalog repository has API, UI, persistence, manifest, generator, and specs, **When** the migration plan is reviewed, **Then** each concern has a target platform package or an explicit deferral.
3. **Given** the old repository will be deprecated, **When** the plan is reviewed, **Then** it defines when deprecation happens and what must be true before it happens.

---

### User Story 2 - Migrate Code Without Losing Behavior (Priority: P1)

Elsa maintainers can import the current package catalog work into `elsa-platform`, keep existing behavior testable, and avoid mixing mechanical movement with architectural rewrites.

**Why this priority**: The catalog already contains substantial working code and specs. The fastest safe path to the ideal end state is preserving behavior first, then improving boundaries.

**Independent Test**: Execute the migration tasks through the point where the catalog solution builds and the existing catalog tests run from the platform repository under the transitional package layout.

**Acceptance Scenarios**:

1. **Given** the catalog repository has existing history, **When** migration starts, **Then** the team attempts a history-preserving import unless it proves impractical.
2. **Given** catalog code is imported, **When** the first migration phase completes, **Then** the code builds and tests run before package renaming or architectural cleanup begins.
3. **Given** a migration step changes behavior, **When** it is reviewed, **Then** the behavior change is isolated from pure move/rename commits where practical.

---

### User Story 3 - Normalize Package Architecture (Priority: P2)

Elsa maintainers can evolve the imported catalog into platform package names and cleaner subsystem boundaries without breaking package manifest safety, catalog approval semantics, or future deployment integration.

**Why this priority**: The current repo has good substance but was not designed as one subsystem inside the platform repo. Renaming and boundary extraction should make future deployment and Runtime Builder integration cleaner.

**Independent Test**: Verify the platform solution contains target projects named `Elsa.Platform.PackageCatalog.*`, `Elsa.Platform.PackageManifests`, and `Elsa.Platform.PackageManifest.Generator*`, with dependency direction enforced by project references and tests.

**Acceptance Scenarios**:

1. **Given** catalog code has been imported, **When** package names are normalized, **Then** Package Catalog projects use `Elsa.Platform.PackageCatalog.*`.
2. **Given** manifest contracts are shared by catalog, generator, deployment validation, and Runtime Builder clients, **When** they are renamed or moved, **Then** they remain dependency-light and independent from catalog persistence, deployment engine internals, and runtime installation internals.
3. **Given** NuGet source ingestion is one source type, **When** boundaries are cleaned up, **Then** NuGet-specific code lives behind a source-provider package rather than in catalog core.
4. **Given** deployment will validate package descriptors, **When** catalog abstractions are defined, **Then** deployment can depend on abstractions or a client package, not catalog API, UI, or persistence internals.

---

### User Story 4 - Deprecate Old Repository Safely (Priority: P2)

Elsa maintainers and contributors know that active package catalog development has moved to `elsa-platform`, and the old `elsa-package-catalog` repository remains useful only as historical reference or an archived redirect.

**Why this priority**: Without a clear deprecation path, issues, pull requests, and docs can split across two repositories.

**Independent Test**: Review the old repository deprecation checklist and confirm it has redirect documentation, issue policy, release/package guidance, and archival criteria.

**Acceptance Scenarios**:

1. **Given** the platform repo contains the usable catalog subsystem, **When** the old repository README is updated, **Then** it points contributors to the new location.
2. **Given** open work remains in the old repository, **When** deprecation starts, **Then** unresolved issues/specs are either migrated, linked, or explicitly closed with rationale.
3. **Given** no active work remains in the old repository, **When** archival is considered, **Then** maintainers can archive it without losing implementation history or contributor guidance.

---

### User Story 5 - Enable Deployment Integration (Priority: P3)

Elsa deployment planning can consume package catalog capabilities for package requirement validation without coupling deployment to catalog internals.

**Why this priority**: The deployment platform roadmap needs package descriptors early, but package installation, approval, trust, and compatibility should be handled through catalog-facing contracts.

**Independent Test**: Validate that the migration plan includes a catalog-facing abstraction or client contract that deployment can later use for package requirement validation, approval state, and compatibility checks.

**Acceptance Scenarios**:

1. **Given** a deployment manifest includes package descriptors, **When** deployment validation is planned, **Then** it uses package catalog abstractions or API/client contracts for package lookup and compatibility.
2. **Given** catalog approval, validity, trust, and compatibility are distinct states, **When** deployment validation consumes catalog data, **Then** those states remain distinct in the contract.
3. **Given** package installation is not Phase 1 deployment scope, **When** catalog integration is planned, **Then** it validates package requirements without forcing package installation behavior.

### Edge Cases

- The catalog repository cannot be imported with full history because of tooling, repository size, or conflicts.
- Existing package IDs have already been published or consumed externally.
- Spec Kit files from the catalog repository conflict with Spec Kit files in `elsa-platform`.
- Existing catalog API routes or UI paths include names that no longer match the platform package naming.
- EF migration namespaces and migration assembly names change during rename.
- Azure deployment configuration refers to old project paths.
- Test fixtures assume old solution names or project paths.
- Old repository issues or specs describe work that is already superseded by the platform roadmap.
- Package manifest generator package identity changes would break package authors.
- Deployment integration tries to reference catalog persistence directly.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform plan MUST define Package Catalog as a sibling subsystem to Deployment, not a child of Deployment.
- **FR-002**: The target source layout MUST include `Elsa.Platform.PackageCatalog.*`, `Elsa.Platform.PackageManifests`, and `Elsa.Platform.PackageManifest.Generator*` projects.
- **FR-003**: The migration MUST attempt to preserve `elsa-package-catalog` git history unless a documented blocker makes a non-history import necessary.
- **FR-004**: The first migration increment MUST preserve current catalog behavior before architectural cleanup begins.
- **FR-005**: Mechanical moves and renames SHOULD be committed separately from behavioral or architectural changes.
- **FR-006**: The platform package manifest contract MUST remain dependency-light and independent from catalog persistence, deployment engine internals, runtime installation internals, and generated code.
- **FR-007**: Catalog ingestion MUST retain the safety rule that it inspects package files and manifest JSON without loading or executing arbitrary package assemblies.
- **FR-008**: NuGet-specific source synchronization MUST live behind a source-provider boundary.
- **FR-009**: Catalog core MUST keep package validity, approval, trust, compatibility, source visibility, and sync state as distinct concepts.
- **FR-010**: Deployment integration MUST depend on package catalog abstractions or client contracts rather than catalog API, UI, or persistence projects.
- **FR-011**: The plan MUST define compatibility handling for existing `Elsa.PackageManifests` and `Elsa.PackageManifest.Generator` package identities before renaming published package IDs.
- **FR-012**: The plan MUST identify EF migration, API route, UI, Azure deployment, and test impacts of project renames.
- **FR-013**: The old `elsa-package-catalog` repository MUST get a deprecation README update after the platform subsystem is usable.
- **FR-014**: Open catalog specs/issues MUST be migrated, linked, or explicitly closed before old repository archival.
- **FR-015**: Spec Kit `tasks.md` MUST be used as the progress tracker for the migration.
- **FR-016**: Phase gates MUST define what must be true before moving from import, to rename, to cleanup, to old-repository deprecation.

### Key Entities *(include if feature involves data)*

- **Platform Subsystem**: A bounded area within `elsa-platform`, such as Deployment or Package Catalog, with source projects, tests, specs, and ownership rules.
- **Package Manifest Contract**: The dependency-light wire contract used by generator, catalog ingestion, deployment validation, and builder clients.
- **Catalog Source Provider**: A source-specific ingestion adapter, initially NuGet, that discovers package metadata and manifests.
- **Compatibility Contract**: A contract that expresses package validity, approval, trust, and compatibility results without leaking catalog persistence.
- **Migration Phase**: A tracked implementation phase with entry criteria, tasks, verification, and exit criteria.
- **Deprecation Notice**: Documentation and repository state that redirects contributors from the old catalog repository to `elsa-platform`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The migration plan identifies every current catalog project and maps it to a target platform project or explicit deferral.
- **SC-002**: The first implementation phase can be verified by building the imported catalog code and running its existing test suites from `elsa-platform`.
- **SC-003**: The final package layout has no deployment project references to catalog API, UI, or persistence projects.
- **SC-004**: Package manifest contract tests continue to pass after namespace/project moves.
- **SC-005**: The old repository deprecation checklist has no unchecked migration blockers before archival is proposed.
- **SC-006**: The `tasks.md` checklist shows phase-level progress and can be used to resume work without rereading the whole conversation.

## Assumptions

- The catalog packages have not yet reached broad external consumption; if they have, compatibility aliases or deprecation packages will be added.
- The team prefers the ideal platform package names soon, even if this creates a larger migration now.
- The old repository remains available during migration and can be updated after the platform repo is usable.
- Source history should be preserved if practical.
- Deployment Phase 1 validates package descriptors but does not install packages.
