# Implementation Plan: Valence Control Self-Healing

**Branch**: `039-valence-control-self-healing` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/039-valence-control-self-healing/spec.md`

## Summary

Deliver a governed self-healing control plane for .NET applications. Valence Control composes Foundation's OTLP receiver, durably accepts post-redaction exception candidates, deduplicates and attributes them using revision-bound component manifests and approved source ownership bindings, projects repair work to GitHub, accepts bounded agent results through a trusted publisher, optionally auto-merges only under a strict low-risk policy, and closes incidents only after positive per-environment deployment verification.

The implementation is split across two coordinated repositories:

- `elsa-foundation` adds one generic additive `IOpenTelemetryIngestionContributor` extension point after redaction. It contains no Healing concepts.
- `valence-control` owns the Healing signal profile, client and component-manifest packages, all incident/repair/policy state, OpenTelemetry bridge, GitHub and agent adapters, API, persistence, workers, and Console experience.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 5.7 with React 18

**Primary Dependencies**: ASP.NET Core minimal APIs; Entity Framework Core 10; Foundation `Elsa.Diagnostics.OpenTelemetry` packages; `HttpClientFactory`; GitHub Copilot SDK behind the no-tools managed inference seam; React Query; existing Valence Control deployment and workspace authorization infrastructure

**Storage**: Healing-owned `HealingDbContext` and SQLite/SQL Server migrations, using the same configured physical database where desired but a separate migration history; Foundation telemetry storage remains an observability concern and is not the Healing queue

**Testing**: xUnit and its built-in assertions, ASP.NET Core `WebApplicationFactory`, EF Core SQLite integration tests, Vitest, React Testing Library, deterministic fake GitHub/inference/deployment adapters

**Target platform**: Linux-hosted Valence Control control plane; .NET and ASP.NET Core monitored applications; GitHub-hosted repositories and GitHub Actions runners

**Project Type**: Cross-repository modular web application with publishable client/build packages

**Performance Goals**: 99% of accepted qualifying exceptions projected to their canonical incident within two minutes; 10,000 duplicate occurrences across 100 instances produce one active incident/work item; bounded worker concurrency and provider calls

**Constraints**: Post-redaction evidence only; workspace isolation; idempotent durable intake; no agent access to Git write credentials; no self-modifying repairs; maximum two automatic attempts by default; Valence Control observes but never deploys or rolls back

**Scale/Scope**: Six end-to-end user stories, 60 functional requirements, 12 measurable outcomes, two repositories, one source provider in v1, multiple applications/environments/revisions per workspace

## Constitution Check

*GATE: Passed before Phase 0 and re-evaluated after Phase 1.*

| Principle | Design evidence | Gate |
|---|---|---|
| I. Control Plane First | Valence Control owns governance, repair orchestration, provider projections, and verification records. Runtime workflow state, deployment, and rollback remain outside Healing. | PASS |
| II. Bounded Subsystems | Healing is a new sibling core project with dependency-light provider contracts. GitHub, API, persistence, UI, and Foundation OTLP transport remain adapters. | PASS |
| III. Contract Stability | Signal profile, component manifest, explicit incident API, workflow protocol, and provider command vocabulary are versioned contracts. | PASS |
| IV. Safety By Design | Manifest generation uses build metadata and hashes without loading customer assemblies. Evidence is redacted; agents have no provider write token; publisher policy is deterministic. | PASS |
| V. Incremental Verifiability | Tasks are organized by independently testable user story, with contract, unit, integration, UI, security, and lifecycle tests. | PASS |

Post-design check: the contracts and data model preserve every boundary above. No constitutional exception is required.

## Architectural Decisions

1. **Durable intake before background analysis**: Foundation calls additive contributors after redaction and before telemetry storage/live publication. Valence Control's contributor performs only an idempotent durable inbox append. Classification, deduplication, attribution, and repair dispatch happen in leased background workers.
2. **Valence Control incident is canonical**: GitHub issues, labels, comments, workflows, branches, and pull requests are projections or commands. They never become the source of truth.
3. **Separate agent and publisher trust zones**: the repair gateway produces a bounded patch/report. A deterministic publisher validates incident lease, base revision, changed paths, patch size, forbidden categories, and required evidence before using provider credentials.
4. **Provider-neutral core, GitHub-only adapter**: Core contracts use work-item, workflow, pull-request, and merge terminology. The GitHub adapter owns labels, webhook signatures, GitHub Actions dispatch, installation tokens, and REST payloads.
5. **Revision-bound component authority**: the application build package produces an immutable component manifest. Workspace-owned source bindings grant repair authority; package metadata can only suggest a binding.
6. **Verification is deployment-observation driven**: merge is an attempted repair. A per-environment result becomes healed only after the repaired revision is observed, the affected operation succeeds, the verification window completes, and no recurrence is seen.
7. **Cross-repository sequencing**: Foundation's generic contribution contract lands first. Valence Control consumes the published package version and verifies the combined OTLP-to-inbox path with a local-package integration lane before Valence Control merge.
8. **Independent persistence ownership**: Healing uses a dedicated context, migrations, stores, and migration history. It may share a physical database and workspace/application identifiers with other Valence Control subsystems without making Package Catalog persistence its owner.

## Project Structure

### Documentation (this feature)

```text
specs/039-valence-control-self-healing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── component-manifest.md
│   ├── console-ux.md
│   ├── github-repair-protocol.md
│   ├── healing-api.md
│   └── signal-profile.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Valence Control

```text
src/
├── ValenceControl.Healing.Abstractions/
├── ValenceControl.Healing.Core/
│   ├── Configuration/
│   ├── Incidents/
│   ├── Manifests/
│   ├── Ownership/
│   ├── Providers/
│   ├── Repairs/
│   ├── Security/
│   └── Verification/
├── ValenceControl.Healing.Agent/
├── ValenceControl.Healing.GitHub/
├── ValenceControl.Healing.OpenTelemetry/
├── ValenceControl.Healing.Client/
├── ValenceControl.Healing.ComponentManifest/
├── ValenceControl.Healing.ComponentManifest.Generator.MSBuild/
├── ValenceControl.Healing.Persistence.EntityFrameworkCore/
├── ValenceControl.Healing.Persistence.SqliteMigrations/
├── ValenceControl.Healing.Persistence.SqlServerMigrations/
├── ValenceControl.Api/Workspace/Healing/
└── ValenceControl.Console/src/features/healing/

tests/
├── ValenceControl.Healing.Abstractions.Tests/
├── ValenceControl.Healing.Core.Tests/
├── ValenceControl.Healing.Agent.Tests/
├── ValenceControl.Healing.GitHub.Tests/
├── ValenceControl.Healing.OpenTelemetry.Tests/
├── ValenceControl.Healing.Client.Tests/
├── ValenceControl.Healing.ComponentManifest.Tests/
├── ValenceControl.Healing.ComponentManifest.Generator.MSBuild.Tests/
├── ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests/
├── ValenceControl.Api.Tests/Healing/
└── ValenceControl.Console/src/features/healing/*.test.tsx
```

### Elsa Foundation coordinated change

```text
src/Elsa/Diagnostics/OpenTelemetry/Core/Contracts/
└── IOpenTelemetryIngestionContributor.cs

src/Elsa/Diagnostics/OpenTelemetry/
├── Services/OpenTelemetryIngestor.cs
├── Extensions/ServiceCollectionExtensions.cs
├── README.md
└── EXTENSION_POINTS.md

tests/Elsa/Diagnostics/OpenTelemetry/Tests/
├── OpenTelemetryIngestorTests.cs
└── OpenTelemetryFeatureTests.cs
```

**Structure Decision**: Healing receives dependency-light abstractions plus explicit core, persistence, agent, OpenTelemetry bridge, and provider adapters. Existing Valence Control host projects remain composition roots for API and Console UI. The Foundation change is deliberately a generic telemetry extension point so neither repository reverses its ownership boundary.

## Delivery Gates

1. Foundation contribution ordering/redaction/failure tests pass and the package is available to Valence Control.
2. Valence Control contracts, models, and persistence migrations pass SQLite and SQL Server model verification.
3. OTLP ingestion proves durable, idempotent Healing inbox acceptance before success response.
4. Incident fingerprint, thresholds, attribution, and one-work-item invariants pass concurrency tests.
5. Agent credential isolation, untrusted-input handling, publisher forbidden-path rules, attempt caps, and auto-merge negative matrix pass.
6. Deployment verification proves no environment or incident can be marked healed on absence-only evidence.
7. API authorization, cross-workspace isolation, webhook authenticity, and audit reconstruction tests pass.
8. Console tests, full .NET solutions, TypeScript typecheck/build, package generation, and cross-repository quickstart pass.
9. Self-review and independent diff review report no actionable findings before PR publication.
10. Foundation PR merges and its artifact is consumable before the dependent Valence Control PR merges.

## Complexity Tracking

No constitution violations require justification. The number of projects reflects explicit package/provider boundaries: core domain, GitHub adapter, public signal client, dependency-light manifest contract, and MSBuild generator each have different consumers and trust surfaces.
