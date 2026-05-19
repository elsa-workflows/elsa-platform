# Research: Elsa Package Catalog

## Decision: Target .NET 10 LTS

**Decision**: Build the solution on .NET 10 LTS and ASP.NET Core 10.

**Rationale**: The project is starting in 2026 and should use the current LTS
line for the longest support window. Microsoft lists .NET 10 as LTS with active
support through November 14, 2028.

**Alternatives considered**:

- .NET 8 LTS: supported but reaches end of support earlier in November 2026.
- .NET 9 STS: supported but not an LTS line and also reaches end of support in
  November 2026.

**Source**: [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)

## Decision: Onion-Style Modular Monolith Project Split

**Decision**: Use separate projects for manifest contracts, catalog core, API,
EF Core persistence, and NuGet packaging.

**Rationale**: The constitution requires modular monolith first, and the project
preference is onion-style layering rather than separate Application and Domain
projects. `Elsa.Catalog.Core` keeps catalog entities, models, invariants, and
small workflow services in the center. API, persistence, and NuGet packaging
stay outside that core. `Elsa.PackageManifests` remains free from catalog
persistence and runtime implementation dependencies.

**Alternatives considered**:

- Separate Application and Domain projects: common in Clean Architecture, but
  heavier than needed for this catalog and not the preferred style.
- Single project: simpler initially but would blur contract, core, API, and
  adapter boundaries.
- Separate services: premature operational complexity for the first version.

## Decision: SQLite First With Provider-Neutral Domain

**Decision**: Use SQLite for initial persistence through EF Core while keeping
catalog core models and service contracts database-provider neutral.

**Rationale**: SQLite satisfies the first-version simplicity goal and supports
local development well. EF Core migrations and explicit relational mappings keep
the path open for PostgreSQL later.

**Alternatives considered**:

- PostgreSQL first: stronger production database but adds operational setup
  before the catalog needs it.
- Document database: weak fit for the relational approval, sync, package-version,
  and query projection model.

## Decision: Store Raw Manifest JSON And Query Projections

**Decision**: Store the raw manifest JSON on package versions, then persist
derived feature and setting projections for public discovery queries.

**Rationale**: Raw manifest storage preserves the wire contract for audit,
revalidation, schema migration, and suspicious-change detection. Derived
projections keep public feature and package queries straightforward without
coupling public reads to ad hoc JSON traversal.

**Alternatives considered**:

- Only raw JSON: simpler writes but harder public filtering and compatibility
  queries.
- Fully normalized manifest graph only: easier relational querying but risks
  losing unknown extension data and future schema compatibility.

## Decision: Root Manifest Path With Fallback

**Decision**: The canonical manifest path is `/elsa-package.json`; the fallback
path is `/build/elsa-package.json`.

**Rationale**: A root file is easy for publishers, tooling, and reviewers to
find. A single fallback allows package-build integration without open-ended file
discovery. If both exist, the root manifest wins and the extra manifest produces
a validation warning.

**Alternatives considered**:

- Search the whole package: too implicit and can produce surprising results.
- Only `build/`: less discoverable to humans inspecting the package.

## Decision: Glob Include/Exclude Patterns For V1

**Decision**: Use case-insensitive glob patterns for package source include and
exclude rules, with exclude rules taking precedence.

**Rationale**: Globs are understandable to administrators, safer than arbitrary
regular expressions, and enough for curated package ID scopes such as
`Elsa.*` or `Acme.Elsa.*`.

**Alternatives considered**:

- Regular expressions: more powerful but easier to misconfigure.
- NuGet prefix conventions only: simple but less flexible for curated sources.

## Decision: Manual Approval For Newly Indexed Versions Unless AutoApprove Source

**Decision**: Package versions from `Manual` sources start as pending, even when
the package itself was previously approved. Package versions from `AutoApprove`
sources can be approved automatically only when valid and not previously
rejected.

**Rationale**: Package-level trust and version-level trust are separate. New
versions can introduce new features or compatibility metadata and deserve their
own review when a source is manual.

**Alternatives considered**:

- Package approval auto-approves all future versions: convenient but weakens
  version-level review.
- Every source is manual: safest but unnecessarily burdens trusted internal
  feeds.

## Decision: 1 MB Manifest Size Limit For V1

**Decision**: Reject manifests larger than 1 MB as validation failures.

**Rationale**: Package manifests should contain metadata, settings schemas, and
documentation links, not large embedded payloads. A 1 MB limit is generous for
metadata while protecting sync memory usage and API diagnostics.

**Alternatives considered**:

- 256 KB: probably sufficient, but tight for large settings schemas.
- 5 MB or larger: little benefit and higher abuse/debugging cost.

## Decision: Validation Warnings Are Admin-Visible, Public Compatibility May Surface Relevant Warnings

**Decision**: Public package and feature listing APIs do not expose validation
internals. Compatibility checks may return warnings relevant to a selected
package set, such as unknown compatibility metadata.

**Rationale**: Public discovery should stay clean and safe. The compatibility API
needs actionable user-facing warnings when a selection has uncertainty.

**Alternatives considered**:

- Expose all warnings publicly: risks confusing normal catalog browsing.
- Hide all warnings publicly: weakens builder feedback for selected packages.

## Decision: API Key Authentication For V1 Admin APIs

**Decision**: Protect admin APIs with an API key scheme for the first version,
implemented in a way that can be replaced or augmented with OpenID Connect later.

**Rationale**: API keys meet the initial admin-protection requirement with low
operational cost. Keeping authorization checks at endpoint/application boundaries
keeps the domain model independent from the auth mechanism.

**Alternatives considered**:

- OpenID Connect first: desirable later but unnecessary setup for the initial
  local/catalog service.
- No admin authentication: violates the security requirements.

## Decision: Basic Compatibility Checks In V1

**Decision**: V1 compatibility checks verify package version existence, listed
state, approval state, manifest validity, requested Elsa version range inclusion
when declared, requested Docker image version range inclusion when declared, and
direct package/feature conflicts when declared. Full dependency resolution is
deferred.

**Rationale**: This provides useful builder feedback while honoring the non-goal
of full dependency resolution.

**Alternatives considered**:

- Full dependency solver: too broad for the first version.
- Existence-only checks: too weak for Runtime Builder readiness.
