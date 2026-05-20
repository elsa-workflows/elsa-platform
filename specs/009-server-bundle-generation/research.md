# Research: Server-Side Bundle Generation

## Decision: Add Protected Builder Bundle Endpoints Beside Existing Builder Routes

Use `POST /api/builder/bundle` for trusted frontend/proxy clients and `POST /api/workspaces/{workspaceId}/builder/bundle` for authenticated workspace clients.

Rationale:

- The existing builder surface already owns catalog and resolve operations under `/api/builder`.
- Lovable can keep anonymous user UX while proxying requests with trusted platform credentials.
- Workspace custom feeds already use workspace-scoped builder routes, so private source visibility needs a workspace variant.

Alternatives considered:

- Make the endpoint directly public: rejected because generation can be expensive and the clarified spec requires protected direct platform calls.
- Require end-user authentication for all bundle generation: rejected because anonymous Runtime Builder remains in scope.
- Create a new deployment module route now: rejected because the first slice is builder bundle generation, not full deployment management.

## Decision: Return Raw Text Files, Not ZIPs Or Stored Download URLs

The first release returns generated files inline as response data and does not store generated ad hoc files.

Rationale:

- Raw files satisfy Lovable preview and download behavior with the least operational surface.
- Ephemeral response data avoids storing user settings or accidental secrets.
- ZIPs and short-lived downloads can be layered on later without changing the core generation service.

Alternatives considered:

- Synchronous ZIP response: rejected for MVP because it complicates preview and is not required by the clarified spec.
- Stored download URL: rejected because the clarified spec says first-release files are ephemeral.
- Permanent bundle storage: deferred to saved configuration versions.

## Decision: Use Deterministic C# Renderers Instead Of A Template Engine

Render `config.json`, `packages.lock.json`, `docker-compose.yml`, `.env.example`, `README.md`, and optional `Program.Generated.cs` with focused C# renderers in the core builder area.

Rationale:

- Avoids introducing a third-party templating dependency before template expansion is needed.
- Keeps rendering behavior easy to test with exact fixture snapshots.
- Matches the constitution's simplicity and dependency discipline.

Alternatives considered:

- Razor/Liquid/Scriban templates: rejected for this slice because template inheritance, escaping, and dependency management are not needed yet.
- Keep generation in frontend and wrap it: rejected because backend deployment truth is the feature goal.

## Decision: Validate Before Rendering And Return Findings-Only For Blocking Errors

Bundle generation first validates the intent, source/package visibility, package compatibility, required settings, image metadata availability, infrastructure selections, and secret handling. Blocking errors return no files.

Rationale:

- The clarified spec requires no files when blocking errors exist.
- Existing compatibility checks already provide package/source/feature findings.
- Separating validation from rendering makes tests easier to target.

Alternatives considered:

- Render partial bundles on errors: rejected by clarification.
- Let clients decide whether generated files are deployable: rejected because backend is now the source of deployment truth.

## Decision: Treat Backend Output As The New Contract

Migration fixtures compare current browser output to backend output only to highlight rollout differences; exact browser parity is not required.

Rationale:

- The browser generator contains deployment truth that should be corrected or simplified during migration.
- The new platform bundle contract is easier to maintain than compatibility with every frontend implementation detail.
- Fixture comparisons still reduce rollout risk by showing visible differences.

Alternatives considered:

- Require exact browser parity: rejected by clarification.
- Skip migration comparison entirely: rejected because it would hide rollout-impacting differences.

## Decision: Minimal Runtime Image Metadata For This Slice

The bundle generator depends on a small runtime image metadata abstraction that can be seeded/configured for the known initial image slugs. The dedicated runtime image metadata API feature will later make this metadata authoritative and externally visible.

Rationale:

- Bundle generation cannot render image names, ports, environment defaults, or companion behavior without image metadata.
- The next Spec Kit feature covers full runtime image metadata ownership, so this slice should avoid overbuilding admin/storage flows.
- A small abstraction keeps later replacement straightforward.

Alternatives considered:

- Block bundle generation until the runtime image metadata feature is fully implemented: rejected because the PRD prioritizes bundle generation first.
- Keep using frontend image data at generation time: rejected because server-side generation must not depend on browser-owned deployment truth.

## Decision: Reuse Existing Visibility And Workspace Access Checks

Public/trusted bundle generation uses public catalog visibility. Workspace bundle generation uses existing workspace membership and workspace-visible package/source queries.

Rationale:

- Source-qualified package identity and workspace-private visibility are already established by prior features.
- Reusing the existing query/services avoids duplicate authorization paths.
- Tests can mirror existing public/workspace builder resolve coverage.

Alternatives considered:

- Add a separate bundle-specific source resolver: rejected because it risks divergence from catalog/resolve visibility.
- Allow clients to submit arbitrary package source URLs for bundle generation: rejected because sources must remain explicit and catalog-indexed.

## Decision: Optional Non-Secret Diagnostics Only

The first release emits non-secret diagnostics through logging and result metadata, but does not add a durable generation-run table.

Rationale:

- The spec allows retaining non-secret diagnostics but requires generated files to remain ephemeral.
- Existing logging is enough for first-slice operational insight.
- Durable bundle generation runs fit better with saved configurations.

Alternatives considered:

- Add a database table for every generation attempt: rejected as premature for anonymous ad hoc generation.
- Store no diagnostics: rejected because operational failures need basic visibility.
