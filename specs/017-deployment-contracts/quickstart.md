# Quickstart: Deployment Foundation Contracts

## Goal

Verify that the first Phase 1 deployment slice adds dependency-light foundation contracts and tests without implementing manifest parsing, artifact IO, engine execution, CLI, API, or runtime adapters.

## Commands

Run focused tests:

```bash
dotnet test tests/Elsa.Platform.Deployment.Abstractions.Tests/Elsa.Platform.Deployment.Abstractions.Tests.csproj
```

Run the full solution:

```bash
dotnet test Elsa.Platform.sln
```

Check dependencies:

```bash
dotnet list src/Elsa.Platform.Deployment.Abstractions/Elsa.Platform.Deployment.Abstractions.csproj reference
```

Expected result:

- Focused tests pass.
- Full solution tests pass.
- Deployment abstractions project has no project references.
- Existing repository warnings remain unchanged unless separately addressed.

## Verification Notes

Last verified on 2026-05-20:

```text
dotnet test tests/Elsa.Platform.Deployment.Abstractions.Tests/Elsa.Platform.Deployment.Abstractions.Tests.csproj
Passed: 28
```

```text
dotnet test Elsa.Platform.sln
Passed all .NET test projects
```

```text
dotnet list src/Elsa.Platform.Deployment.Abstractions/Elsa.Platform.Deployment.Abstractions.csproj reference
There are no Project to Project references.
```

```text
git diff --check
Passed
```

Known pre-existing warning:

- `Microsoft.Build.Utilities.Core` 17.14.8 emits NU1903 for GHSA-w3q9-fxm7-j8fq in the package manifest generator MSBuild project and tests.

## Manual Review Checklist

- `src/Elsa.Platform.Deployment.Abstractions/` contains only contracts and value types.
- `tests/Elsa.Platform.Deployment.Abstractions.Tests/` covers identity, artifacts, diagnostics, plans, results, history, extension points, and boundaries.
- `Elsa.Platform.sln` includes the new source and test projects.
- `docs/deployment-platform-phased-strategy.md` still marks manifest parsing, artifact IO, engine, CLI, and API implementation as later Phase 1 work.

## Deferred Follow-Up Slices

1. Manifest v1alpha parsing and normalization.
2. Folder and ZIP artifact IO.
3. Engine planning, validation, diff, dry-run, apply, and in-memory history.
4. CLI build, inspect, validate, diff, dry-run, apply, and history commands.
5. Optional thin API wrapping the same engine contracts.
