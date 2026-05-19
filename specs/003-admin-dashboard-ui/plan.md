# Implementation Plan: Elsa Package Catalog Admin Dashboard UI

**Branch**: `codex/004-admin-dashboard-ui` | **Date**: 2026-05-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-admin-dashboard-ui/spec.md`

## Summary

Build a small internal admin web UI for Elsa Package Catalog operations. The UI
uses the existing authenticated Catalog Admin REST APIs for package source
management, package-version approval, validation inspection, manifest
inspection, and sync-run troubleshooting. The first slice intentionally exposes
only four destinations: Overview, Sources, Packages, and Sync Runs. Settings,
advanced RBAC, analytics dashboards, realtime streaming, and direct manifest
editing remain out of scope.

The implementation adds a focused frontend project plus small Catalog Admin API
contract updates where the clarified spec needs backend support: source
soft-delete semantics, source health guarantees, version-only approval affordance
for the UI, required rejection reasons, pattern-test support, and enough package
version detail for manifest/validation/visibility explanation screens.

## Technical Context

**Language/Version**: TypeScript for the admin UI; existing backend remains C# on
.NET 10 LTS with ASP.NET Core.

**Primary Dependencies**: React, React Router, TanStack Query, TailwindCSS,
shadcn/ui-style component composition, existing Catalog Admin REST APIs, existing
`Elsa.PackageManifests` manifest JSON contract.

**Storage**: No frontend-owned durable storage. The dashboard may keep transient
UI state in memory and URL query parameters. Durable source, package, approval,
validation, manifest, and sync data remains in the existing catalog persistence
layer.

**Testing**: Frontend unit/component tests with a TypeScript test runner and DOM
testing utilities, API-contract adapter tests with mocked HTTP responses,
end-to-end smoke tests against the local Catalog API, plus existing .NET xUnit
tests for any admin API contract changes.

**Target Platform**: Modern browsers used by internal administrators; local
development against the ASP.NET Core Catalog API and Aspire app host where
useful.

**Project Type**: Web application frontend consuming an existing modular
monolith API.

**Performance Goals**: Initial list screens render usable content within 2
seconds for up to 100 sources, 5,000 packages, and 1,000 recent sync runs when
the admin API responds normally. Filtering/search should preserve perceived
responsiveness with server-backed query parameters where available.

**Constraints**: Four MVP navigation destinations only; no Settings screen; no
direct manifest editing; no realtime infrastructure requirement; polling/manual
refresh only; source status and last successful sync are the only guaranteed
source health fields; package approval controls operate on package versions
only; rejection requires a reason; source removal is soft-delete only.

**Scale/Scope**: Internal operational UI for a small admin audience, covering
source lifecycle, package-version approval, package diagnostics, sync history,
and a lightweight overview. Not an enterprise analytics, observability, or
workflow platform.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Manifest-first**: PASS. The UI displays manifest-derived metadata and raw
  `elsa-package.json`; it does not infer package behavior from implementation
  code.
- **No arbitrary code execution**: PASS. The dashboard consumes admin API JSON
  and never loads package assemblies or executes package code.
- **Stable contracts**: PASS. No changes are planned for
  `Elsa.PackageManifests`; UI contracts consume existing manifest JSON and
  validation DTOs.
- **Schema evolution**: PASS. Manifest schema versions are displayed as catalog
  data. The UI does not define schema evolution rules.
- **Immutable versions**: PASS. Package-version details surface manifest hashes
  and suspicious-change state without overwriting version content.
- **Approval separation**: PASS. The UI treats approval, validation, listing,
  and suspicious state as separate status signals, and approval actions target
  package versions only.
- **Explicit sources**: PASS. Source workflows keep include/exclude patterns and
  a pattern tester central to source management.
- **Safe public API**: PASS. This is an admin-only surface; visibility
  explanations explicitly distinguish admin records from public-safe packages.
- **Debuggability**: PASS. The plan includes validation findings, manifest
  inspection, sync run details, and source health derived from guaranteed fields
  plus recent sync diagnostics.
- **Modular monolith**: PASS. The UI is a frontend client for the existing
  modular monolith APIs and introduces no distributed infrastructure.
- **Runtime Builder readiness**: PASS. The dashboard helps curate package
  metadata that future builder tooling depends on, without becoming the builder
  UI.
- **Simplicity**: PASS. The MVP uses one frontend project, a thin API client,
  simple polling, and no Settings, plugin, streaming, analytics, or advanced
  RBAC surfaces.

Post-design re-check: PASS. Phase 1 artifacts preserve the small operational
scope, keep backend contracts explicit, and avoid new infrastructure beyond a
single web UI project and focused admin API deltas.

## Project Structure

### Documentation (this feature)

```text
specs/003-admin-dashboard-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── admin-api-ui-contract.md
│   └── ui-routes.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Elsa.Catalog.AdminUi/
│   ├── src/
│   │   ├── app/
│   │   ├── components/
│   │   ├── features/
│   │   │   ├── overview/
│   │   │   ├── sources/
│   │   │   ├── packages/
│   │   │   └── sync-runs/
│   │   ├── lib/
│   │   │   ├── api/
│   │   │   ├── query/
│   │   │   └── status/
│   │   └── test/
│   ├── package.json
│   ├── vite.config.ts
│   └── tailwind.config.ts
├── Elsa.Catalog.Api/
│   └── Admin/
└── existing catalog projects unchanged unless admin API contract deltas require
    small source, approval, validation, or sync endpoint updates

tests/
├── Elsa.Catalog.Api.Tests/
└── Elsa.Catalog.AdminUi.E2E/
```

**Structure Decision**: Add one dedicated frontend project under
`src/Elsa.Catalog.AdminUi` so the UI can evolve independently from the ASP.NET
Core API while still living inside the same repository and release workflow.
Shared frontend code is organized by operational feature area and a thin `lib/api`
adapter layer. Backend changes remain in the existing `Elsa.Catalog.Api`,
`Elsa.Catalog.Core`, and persistence test projects only when needed to satisfy
the UI contract.

## Complexity Tracking

No constitution violations are introduced. The separate frontend project is not
tracked as a violation because the feature is itself a browser UI and keeping UI
code out of the API project reduces coupling while preserving a simple modular
monolith backend.
