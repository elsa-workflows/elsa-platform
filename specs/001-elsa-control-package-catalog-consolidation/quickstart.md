# Quickstart: Elsa Control Package Catalog Consolidation

This quickstart describes how to validate each migration phase.

## 1. Planning Baseline

```bash
git status --short --branch
find specs/001-elsa-control-package-catalog-consolidation -maxdepth 3 -type f | sort
```

Expected:

- Spec Kit files exist.
- `tasks.md` is present and unchecked at the start of implementation.
- Working tree only contains intentional planning changes.

## 2. Import Validation

After importing the catalog repository:

```bash
dotnet restore
dotnet test
```

If the platform solution is split into solution filters, run the catalog solution or filter first, then the platform solution.

Expected:

- Current catalog projects restore.
- Existing catalog tests pass or failures are documented as pre-existing.
- PR #36 Runtime Builder tests are included in the baseline result.

Phase 4 verification log, 2026-05-19:

- Old repository baseline at `e321965a09cdcf63bb6ad3144badd9a203c10da8`: `dotnet test Elsa.PackageCatalog.sln` passed. The run reported the existing `Microsoft.Build.Utilities.Core` NU1903 advisory warnings.
- Old repository console baseline: `npm install --prefix src/Elsa.Catalog.AdminUi` then `npm test --prefix src/Elsa.Catalog.AdminUi -- --run` passed with 11 files and 64 tests. `npm install` reported 5 moderate vulnerabilities and React Router v7 future-flag warnings during tests.
- Elsa Control import verification: `dotnet test ElsaControl.sln` passed from `elsa-control`, including PR #36 builder/runtime tests. The same NU1903 advisory warnings were present.
- Elsa Control console verification: `npm install --prefix src/ElsaControl.Console && npm test --prefix src/ElsaControl.Console -- --run` passed with 11 files and 64 tests. The same npm audit and React Router warnings were present.
- Merge conflicts were limited to expected repository bootstrap files. `elsa-control` Spec Kit state, `AGENTS.md`, `README.md`, and `LICENSE` were kept as authoritative.

## History-Preserving Import Candidate

Preferred import path:

```bash
git remote add package-catalog https://github.com/elsa-workflows/elsa-package-catalog.git
git fetch package-catalog main
git merge --allow-unrelated-histories --no-commit package-catalog/main
```

Before committing the merge, resolve path conflicts by keeping `elsa-control` Spec Kit infrastructure active and moving catalog files into their target platform locations. If a full unrelated-history merge proves too noisy, use a subtree-style import and document the fallback:

```bash
git subtree add --prefix imported/elsa-package-catalog https://github.com/elsa-workflows/elsa-package-catalog.git main
```

The migration should not overwrite:

- `.specify/`
- `AGENTS.md`
- `docs/elsa-control-deployment-phased-strategy.md`
- `specs/001-elsa-control-package-catalog-consolidation/`

PR #36 import note:

- Import from `e321965a09cdcf63bb6ad3144badd9a203c10da8` or newer.
- Keep specs `009` through `016`; they are relevant to Runtime Builder and later platform phases.
- Do not preserve old `.specify/feature.json` as the platform active feature pointer.

## 3. Rename Validation

After platform package renaming:

```bash
dotnet restore
dotnet test
```

Expected:

- Renamed project references resolve.
- Manifest contract tests pass.
- Catalog API/core/persistence tests pass.
- Runtime Builder bundle, planner, runtime image, deployment template, and runtime configuration tests pass.

Phase 5 verification log, 2026-05-19:

- `Elsa.PackageCatalog.sln` was renamed to `ElsaControl.sln`.
- Source, test, namespace, package, and console package names were normalized to `ElsaControl.PackageCatalog.*`, `Elsa.Specifications.PackageManifests`, and `Elsa.Specifications.PackageManifest.Generator*`.
- `ElsaControl.PackageCatalog.Abstractions` was added with compatibility validation request/result contracts. Core and API reference it directly; entity and persistence contracts remain in Core/Persistence until a later deployment-facing boundary needs them.
- `dotnet test ElsaControl.sln` passed after renames and abstraction extraction. The run still reports the existing `Microsoft.Build.Utilities.Core` NU1903 advisory warnings.
- `npm install --prefix src/ElsaControl.Console` reported 5 moderate npm audit findings.
- `npm test --prefix src/ElsaControl.Console -- --run` passed with 11 files and 64 tests. React Router v7 future-flag warnings remain pre-existing.
- Runtime Builder code from PR #36 still lives under Package Catalog projects in this phase and is intentionally scheduled for Phase 6 extraction.

Phase 6 verification log, 2026-05-19:

- Added `ElsaControl.RuntimeBuilder.Abstractions`, `ElsaControl.RuntimeBuilder.Core`, and `ElsaControl.RuntimeBuilder.DeploymentTemplates`.
- Moved runtime builder intent, bundle, runtime image, planner, deployment template, and runtime configuration contracts out of Package Catalog Core.
- Moved Runtime Builder core services and file renderers into `ElsaControl.RuntimeBuilder.Core`.
- Moved deployment template renderers into `ElsaControl.RuntimeBuilder.DeploymentTemplates`.
- Added catalog-facing query and compatibility contracts to `ElsaControl.PackageCatalog.Abstractions` so Runtime Builder Core references catalog abstractions, not catalog core, API, or EF persistence.
- Kept the current HTTP endpoints hosted by `ElsaControl.Api` for now; extracting `ElsaControl.RuntimeBuilder.Api` remains a later API packaging step.
- Kept EF runtime-configuration storage in the catalog EF project as the current host database adapter, while moving runtime configuration models and store interface to Runtime Builder abstractions.
- `dotnet build ElsaControl.sln` passed. The run still reports the existing `Microsoft.Build.Utilities.Core` NU1903 advisory warnings.
- `dotnet test ElsaControl.sln` passed. Runtime Builder tests now run under `tests/ElsaControl.RuntimeBuilder.Core.Tests` with 27 tests.
- `npm test --prefix src/ElsaControl.Console -- --run` passed with 11 files and 64 tests. React Router v7 future-flag warnings remain pre-existing.

## 4. Console Validation

After UI path moves:

```bash
npm install --prefix src/ElsaControl.Console
npm test --prefix src/ElsaControl.Console
```

Expected:

- Unit tests pass.
- E2E tests are either run or explicitly deferred with reason.

## 5. Dependency Boundary Validation

Inspect project references:

```bash
rg "<ProjectReference" src tests
```

Expected:

- Deployment projects do not reference catalog API, UI, persistence, migrations, AppHost, or source-provider internals.
- Package manifest project has no catalog/deployment/persistence/hosting references.

## 6. Old Repository Deprecation Validation

Before old repo archival:

```bash
gh issue list --repo elsa-workflows/elsa-package-catalog --state open
gh pr list --repo elsa-workflows/elsa-package-catalog --state open
```

Expected:

- Open work is migrated, linked, or closed with rationale.
- README points to `elsa-control`.

Phase 7 verification log, 2026-05-19:

- `gh issue list --repo elsa-workflows/elsa-package-catalog --state open --limit 100` returned `[]`.
- `gh pr list --repo elsa-workflows/elsa-package-catalog --state open --limit 100` returned `[]`.
- Old repository README deprecation notice was committed and pushed to `elsa-workflows/elsa-package-catalog` main as `cf7411d`.
- Archive is deferred until the platform consolidation branch is merged and maintainers confirm no private/internal consumers still depend on the old repository receiving direct updates.

## 7. Deployment Integration Contract Validation

```bash
dotnet test ElsaControl.sln
```

Expected:

- `ElsaControl.PackageCatalog.Abstractions` exposes deployment-facing package requirement validation contracts.
- Deployment package projects, when present, can reference catalog abstractions without referencing catalog API, Console, persistence, migrations, source-provider internals, or AppHost.
- Package requirement validation result shape keeps discovery, manifest validity, approval, trust, suspicious-change, compatibility, feature, and conflict findings distinct.

Phase 8 verification log, 2026-05-19:

- Added `IDeploymentPackageCatalog` and package requirement validation DTOs under `ElsaControl.PackageCatalog.Abstractions.Deployment`.
- Added `tests/ElsaControl.PackageCatalog.Abstractions.Tests`.
- Added a boundary test that scans `src/ElsaControl.Deployment.*` project references and fails if future Deployment projects reference catalog internals.
- Added contract tests proving approval, trust, suspicious-change, and compatibility states remain distinct.
- Updated `docs/elsa-control-deployment-phased-strategy.md` to state that Deployment consumes deployment-specific manifests/artifacts and catalog validation contracts, not Runtime Builder intent directly.
- `dotnet test ElsaControl.sln` passed. The existing `Microsoft.Build.Utilities.Core` NU1903 advisory warnings remain.

## 8. Final Readiness Validation

Phase final verification log, 2026-05-19:

- `dotnet test ElsaControl.sln` passed.
- `npm test --prefix src/ElsaControl.Console -- --run` passed with 11 files and 64 tests.
- Active source/docs old-name scan found only intentionally historical old-repo baseline references in this quickstart.
- Added ADR-0002 for package identity compatibility.
- Added ADR-0003 for standalone package catalog repository deprecation.
