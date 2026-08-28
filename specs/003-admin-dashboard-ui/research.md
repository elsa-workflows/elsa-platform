# Research: Elsa Control Package Catalog Console UI

## Decision: Add a dedicated admin frontend project

**Rationale**: The repository currently has ASP.NET Core API, core, persistence,
packaging, and manifest generator projects but no browser UI. A dedicated
`src/ElsaControl.Console` project keeps React/TypeScript build tooling,
components, route definitions, and UI tests separate from API concerns while
remaining in the same monorepo and local development flow.

**Alternatives considered**:

- Embed UI files directly in `ElsaControl.Api`: rejected because it mixes
  frontend build concerns with API endpoints and makes UI testing/dependencies
  noisier.
- Create an external repository: rejected because this is an internal operator
  UI tightly coupled to admin API contracts and Spec Kit planning.

## Decision: Use feature folders with a thin shared API adapter

**Rationale**: Overview, Sources, Packages, and Sync Runs are the only MVP
destinations. Feature folders keep route-level state, tables, forms, and detail
views close to their workflow. A small shared API adapter centralizes auth
headers, request errors, JSON parsing, pagination/query parameters, and mutation
result handling.

**Alternatives considered**:

- Large shared component/service architecture: rejected as too heavy for the
  deliberately small UI.
- Page-only files with duplicated request logic: rejected because it would make
  error handling, stale state, and auth behavior inconsistent.

## Decision: Treat source health as a minimal contract plus inferred diagnostics

**Rationale**: The clarified spec guarantees only source status and last
successful sync. Validation failure counts, authentication failures,
connectivity issues, and richer health messages are useful but should be derived
from recent sync runs or explicit diagnostics when available. This prevents the
UI from forcing a broad health model into the admin API.

**Alternatives considered**:

- Require a rich source health endpoint: rejected because it expands API scope
  toward monitoring.
- Infer everything from sync history: rejected because operators need stable
  source status and last successful sync on list/detail screens.

## Decision: Package approval UI targets package versions only

**Rationale**: Package versions are immutable catalog records and public
visibility depends on version validity, listing, and approval. Version-only UI
approval prevents accidental trust of future versions and aligns approval with
the record operators inspect.

**Alternatives considered**:

- Package identity approval: rejected because it can imply trust across versions.
- Both package and version controls: rejected for MVP because it creates
  overlapping states and more UI explanations.

## Decision: Require rejection reasons

**Rationale**: Rejection is a trust-changing decision. A short required reason
improves auditability, support conversations with package publishers, and
operator confidence without introducing a complex review workflow.

**Alternatives considered**:

- Optional reasons: rejected because silent rejections are hard to explain later.
- Reasons only for bulk rejection: rejected because single-item rejection still
  has the same audit need.

## Decision: Source removal is soft-delete only

**Rationale**: Sources may have package, validation, and sync history. Soft-delete
removes a source from active operation without implying historical records were
erased or orphaned. The UI must not expose hard-delete controls in the MVP.

**Alternatives considered**:

- Hard-delete with confirmation: rejected because retention and historical
  package relationships are not explicit enough.
- Disable-only: rejected because operators need a way to remove obsolete sources
  from active source management.

## Decision: Polling and manual refresh instead of realtime streaming

**Rationale**: Sync activity and dashboard status can be understood through
periodic refresh and explicit manual refresh. This keeps infrastructure simple
and avoids turning Sync Runs into a live observability console.

**Alternatives considered**:

- Websocket or server-sent events: rejected as unnecessary for the operational
  MVP.
- No polling: rejected because active sync runs and overview status would feel
  stale.

## Decision: Pattern tester mirrors catalog glob semantics client-side and can be verified against backend behavior

**Rationale**: The tester is an immediate UX guardrail for source configuration.
The first catalog version uses case-insensitive glob matching with excludes
taking precedence, so the UI can preview results instantly and tests can compare
with backend pattern matcher fixtures.

**Alternatives considered**:

- Server-only tester endpoint: useful later, but unnecessary if UI tests verify
  parity with the documented matcher behavior.
- Freeform regex: rejected because catalog source patterns are glob-based.

## Decision: Test with component/API adapter coverage plus a small E2E smoke suite

**Rationale**: Most risk is in state presentation, filters, mutation
confirmation, validation rendering, and error handling. Component and adapter
tests cover those cheaply, while E2E tests verify the four primary workflows
against a running local API.

**Alternatives considered**:

- E2E-only testing: rejected as slow and brittle for table/filter edge cases.
- Unit-only testing: rejected because route-level workflows and admin API
  integration need browser-level validation.
