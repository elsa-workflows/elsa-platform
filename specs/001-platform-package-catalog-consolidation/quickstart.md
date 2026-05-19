# Quickstart: Platform Package Catalog Consolidation

This quickstart describes how to validate each migration phase.

## 1. Planning Baseline

```bash
git status --short --branch
find specs/001-platform-package-catalog-consolidation -maxdepth 3 -type f | sort
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

## History-Preserving Import Candidate

Preferred import path:

```bash
git remote add package-catalog https://github.com/elsa-workflows/elsa-package-catalog.git
git fetch package-catalog main
git merge --allow-unrelated-histories --no-commit package-catalog/main
```

Before committing the merge, resolve path conflicts by keeping `elsa-platform` Spec Kit infrastructure active and moving catalog files into their target platform locations. If a full unrelated-history merge proves too noisy, use a subtree-style import and document the fallback:

```bash
git subtree add --prefix imported/elsa-package-catalog https://github.com/elsa-workflows/elsa-package-catalog.git main
```

The migration should not overwrite:

- `.specify/`
- `AGENTS.md`
- `docs/deployment-platform-phased-strategy.md`
- `specs/001-platform-package-catalog-consolidation/`

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

## 4. Admin UI Validation

After UI path moves:

```bash
npm install --prefix src/Elsa.Platform.PackageCatalog.AdminUi
npm test --prefix src/Elsa.Platform.PackageCatalog.AdminUi
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
- README points to `elsa-platform`.
