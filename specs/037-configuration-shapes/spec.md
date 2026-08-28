# Feature Specification: Configuration Shapes

**Feature Branch**: `037-configuration-shapes`

**Created**: 2026-07-04

**Status**: Draft

**Input**: User description: "An application has a set of configuration keys that stay consistent across environments while values differ per environment — except settings like secret references that stay constant while the secret value lives in each environment's own store. Shapes target engine-host configuration (IConfiguration), not workflow variables (which travel inside workflow artifacts) and not Loom recipe parameters (out of scope). The platform delivers values through the existing engine sync channel now, with infra adapters (ConfigMaps, App Service settings) as a later phase."

## Design Decisions (resolved via interview, 2026-07-04)

1. **Consumer**: Engine-host `IConfiguration` surface only. Workflow variables ship inside workflow artifacts; Loom recipe parameters are explicitly out of scope for v1.
2. **Delivery**: Hybrid — values ride the existing desired-state / runtime command sync channel now; the value model stays target-agnostic so per-target infra adapters (Kubernetes ConfigMaps/Secrets, App Service settings) can be added later without reshaping the domain. elsa-foundation gains a platform `IConfigurationSource` that surfaces delivered sections to the host.
3. **Constancy model**: Per-key **default + override policy**, not distinct key kinds. Each key: type, required, optional shape-level default, policy = `Locked` | `Overridable` | `RequiredPerEnvironment`. Secret-ness is an orthogonal flag.
4. **Key origin**: Layered, auto-extending — package feature settings auto-contribute keys (reusing the mined `FeatureSettingRecord` schema), artifacts may declare expected keys, admins add custom keys. Every key is origin-tagged (`feature-setting` | `artifact-declared` | `admin`).
5. **Value storage**: Per-environment values live **inside the desired-state revision** as a new `DesiredStateRecordKind` (working name `ConfigurationBinding`). Every value change is a new revision deployed through the normal confirm→run→apply pipeline. The UI must compress "edit value → deploy" into one guided action.
6. **Loom recipes**: Out of scope. Revisit after v1; keys are namespaced so a later `recipe-declared` origin can attach.
7. **Secret references**: Opaque, **engine-resolved**. The per-environment "value" of a secret key is a reference string; the platform never stores or resolves secret values (consistent with spec 035's engine-credential-only scoping). The engine resolves references against its own secret provider and reports resolvability per reference via the existing sync/health channel; promotion validation consumes that report.
8. **Validation**: Enforce at the gates (revision creation, promotion, deploy) as blockers — missing required value, type mismatch, unresolvable secret reference. Between gates each environment carries an **advisory conformance status** (parallel to drift status). Shape definitions are content-hashed and versioned; each revision records the shape version it was validated against. Orphan values (env supplies a key the shape no longer declares) are warnings, not blockers.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Define an Application Configuration Shape (Priority: P1)

A deployment administrator views their application's configuration shape: keys auto-contributed by package feature settings, keys declared by submitted artifacts, and admin-added custom keys — each with type, required flag, optional default, override policy, and secret flag.

**Why this priority**: The shape is the contract everything else validates against. Without it there is no consistent keyspace across environments.

**Independent Test**: Register an application whose artifact/packages contribute typed settings, add a custom key, and verify the composed shape lists all keys with correct origin tags, types, policies, and secret flags.

**Acceptance Scenarios**:

1. **Given** an application whose packages include features with mined setting schemas, **When** the shape is viewed, **Then** those settings appear as shape keys with origin `feature-setting`, carrying their JSON type, validation, required flag, default, and secret flag.
2. **Given** an artifact that declares expected configuration keys, **When** it is registered for the application, **Then** those keys join the shape with origin `artifact-declared` automatically.
3. **Given** an administrator, **When** they add a custom key with type, policy, optional default, and secret flag, **Then** the key joins the shape with origin `admin`.
4. **Given** any shape change, **When** the shape is fetched, **Then** it exposes a new content-hash/version and the previous version remains resolvable.

---

### User Story 2 - Supply Per-Environment Values (Priority: P1)

An operator supplies values for an environment: `RequiredPerEnvironment` keys must be filled per environment; `Overridable` keys show the shape default and may be overridden; `Locked` keys display the default read-only; secret keys accept a reference string, never a secret value.

**Why this priority**: This is the day-to-day surface; the constancy guarantees (keys constant, values vary, secret references constant) are realized here.

**Independent Test**: For one application with dev/test/prod, supply values in dev, verify locked keys cannot be overridden, secret keys accept only references, and the resulting value set is stored as a `ConfigurationBinding` record in a new desired-state revision.

**Acceptance Scenarios**:

1. **Given** a shape with a `Locked` key with default, **When** an operator edits environment values, **Then** the key is visible but not editable and the default applies in every environment.
2. **Given** an `Overridable` key with default `30`, **When** dev overrides it to `5` and prod leaves it untouched, **Then** dev's binding carries `5`, prod's carries the default, and the shape default remains `30`.
3. **Given** a secret-flagged key, **When** an operator supplies a value, **Then** only a reference string is accepted and stored; no secret value is collected, displayed, or logged.
4. **Given** completed value entry, **When** the operator saves, **Then** a new desired-state revision is created containing the environment's `ConfigurationBinding` record, content-hashed, recording the shape version it was validated against.

---

### User Story 3 - Deliver Configuration to the Engine (Priority: P1)

When a revision containing a configuration binding is deployed, the engine receives the materialized configuration through the existing runtime command sync channel and surfaces it to the host as an `IConfiguration` source; secret references are resolved engine-side against the engine's own secret provider.

**Why this priority**: Without delivery the shape is only documentation. Riding the existing channel avoids new infra adapters in v1.

**Independent Test**: Deploy a revision with a binding to a registered engine; verify the engine receives the config payload, surfaces non-secret values via `IConfiguration`, resolves secret references locally, and reports per-reference resolvability back to the platform.

**Acceptance Scenarios**:

1. **Given** a deployed revision with a configuration binding, **When** the engine syncs, **Then** it receives the materialized key/value set (secret keys as references) as part of the applied desired state.
2. **Given** a received configuration payload, **When** the engine host builds configuration, **Then** platform-delivered sections are available via `IConfiguration` with documented precedence relative to local `appsettings`/environment variables.
3. **Given** secret references in the payload, **When** the engine applies, **Then** each reference is resolved against the engine's own provider and per-reference resolvability status is reported to the platform without exposing values.

---

### User Story 4 - Promotion Carries Values by Policy (Priority: P1)

Promoting a revision from dev to prod carries `Locked` and defaulted values automatically, keeps `RequiredPerEnvironment` values environment-local, and blocks with actionable diagnostics when the target environment is missing required values or has unresolvable secret references.

**Why this priority**: This is the feature's reason to exist — keys consistent across environments, values environment-owned, enforced at the promotion gate.

**Independent Test**: Promote a revision into an environment missing one required value and one resolvable secret reference; verify one blocker names the missing key, promotion succeeds after the value is supplied, and locked/default values carried over without re-entry.

**Acceptance Scenarios**:

1. **Given** a promotion preview into a target environment, **When** the target lacks a value for a `RequiredPerEnvironment` key, **Then** the preview reports a blocker naming the key and the target environment.
2. **Given** `Locked` and defaulted `Overridable` keys, **When** promotion completes, **Then** their values carry into the target revision without operator re-entry.
3. **Given** a secret key whose reference the target engine reported unresolvable, **When** promotion or deploy is previewed for a tier requiring secret verification, **Then** a blocker identifies the reference and engine.
4. **Given** all gates pass, **When** promotion completes, **Then** the target revision records the shape version validated against.

---

### User Story 5 - Conformance Status and Shape Evolution (Priority: P2)

When the shape changes (new artifact-declared required key, admin edit), environments that predate the change show an advisory conformance status; nothing is marked failed until a gate is attempted.

**Why this priority**: Auto-extension (US1) means shapes change routinely; operators need to see divergence without false alarms.

**Independent Test**: Add a required key to a shape after environments are deployed; verify environments show "missing 1 required key for shape vN" advisory, existing deployments remain healthy, and the next promotion/deploy into a non-conforming environment blocks.

**Acceptance Scenarios**:

1. **Given** a shape gains a required key, **When** environments are listed, **Then** environments without a value show an advisory conformance indicator naming the missing key and shape version — not a failure state.
2. **Given** an environment supplies a value for a key the shape no longer declares, **When** conformance is evaluated, **Then** an orphan-value warning is shown and no gate is blocked by it.
3. **Given** an environment's conformance advisory, **When** an operator opens it, **Then** they can navigate directly to value entry pre-filtered to the missing keys.

### Edge Cases

- A feature setting and an admin key collide on the same configuration key path (origin precedence and conflict surfacing).
- An artifact version stops declaring a key that another origin still declares.
- A shape default changes while environments hold explicit overrides equal to the old default.
- An engine is offline and cannot report secret-reference resolvability when a tier requires verification (stale verification vs. hard block).
- Two environments share one engine (binding scoping per environment/application on a shared runtime).
- A configuration payload exceeds the sync channel's practical size.
- A key's type changes (e.g., string → int) while environments hold now-invalid values.
- The same key is present in engine-local `appsettings` and platform-delivered config (precedence must be deterministic and documented).
- A rollback re-applies a revision validated against an older shape version.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST maintain one configuration shape per application, composed of keys contributed by package feature settings, artifact declarations, and administrators, each key tagged with its origin.
- **FR-002**: Each shape key MUST carry: key path, JSON type + validation (reusing the feature-setting schema model), required flag, optional shape-level default, override policy (`Locked` | `Overridable` | `RequiredPerEnvironment`), secret flag, and description.
- **FR-003**: Feature-setting and artifact-declared keys MUST join the shape automatically; removal of a contributing origin MUST NOT silently delete a key that another origin or an environment value still references (mark orphaned instead).
- **FR-004**: Shape definitions MUST be content-hashed and versioned; prior versions MUST remain resolvable for audit and revision validation records.
- **FR-005**: Per-environment values MUST be stored as a `ConfigurationBinding` desired-state record inside the environment's revision, content-hashed, and MUST record the shape version validated against.
- **FR-006**: `Locked` keys MUST NOT accept environment overrides; `Overridable` keys MUST fall back to the shape default when not overridden; `RequiredPerEnvironment` keys MUST have an explicit value in every environment before gates pass.
- **FR-007**: Secret-flagged keys MUST accept only opaque reference strings; the platform MUST NOT collect, store, resolve, display, or log secret values (engine credentials per spec 035 remain a separate concern).
- **FR-008**: Materialized configuration MUST be delivered to engines through the existing desired-state / runtime command sync channel; the payload model MUST remain target-agnostic to admit later infra adapters without domain changes.
- **FR-009**: Engines MUST be able to report per-reference secret resolvability; validation MUST consume these reports for tiers requiring secret verification.
- **FR-010**: Revision creation, promotion, and deploy gates MUST block on: missing required values, type/validation violations, and (per tier) unresolvable secret references — each with diagnostics naming key, environment, and remediation.
- **FR-011**: Environments MUST expose an advisory conformance status against the current shape version (missing keys, orphan values as warnings) without failing any gate until one is attempted.
- **FR-012**: Promotion MUST carry `Locked` and defaulted values into the target revision automatically and keep `RequiredPerEnvironment` values environment-local.
- **FR-013**: All shape and value mutations MUST be auditable (actor, timestamp, before/after hashes) consistent with existing deployment mutation records.

### Key Entities

- **ConfigurationShape**: Application-scoped aggregate; versioned, content-hashed set of ConfigurationShapeKeys.
- **ConfigurationShapeKey**: Key path, schema (type/validation/required/default), override policy, secret flag, origin tag, lifecycle state (active/orphaned).
- **ConfigurationBinding** (new `DesiredStateRecordKind`): An environment's value set — explicit values, secret references, and the shape version validated against — carried inside a desired-state revision.
- **SecretReferenceResolvabilityReport**: Engine-reported per-reference status consumed by validation.

## Out of Scope (v1)

- Loom recipe parameter binding or contribution (future `recipe-declared` origin reserved).
- Workflow variable management (travels inside workflow artifacts).
- Infra materialization adapters (ConfigMaps, App Service settings) — phase 2 of delivery.
- Elsa Control-side environment secret store registries, vault browsing/pickers, or secret value storage.
- Cross-application or workspace-level shared shapes/overlays.
