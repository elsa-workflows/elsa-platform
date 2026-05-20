# Quickstart: Deployment Manifest Parsing

## Goal

Verify that v1alpha manifests parse and normalize independently from artifact IO or engine execution.

## Commands

Run focused tests:

```bash
dotnet test tests/Elsa.Platform.Deployment.Manifest.Tests/Elsa.Platform.Deployment.Manifest.Tests.csproj
```

Run full solution tests:

```bash
dotnet test Elsa.Platform.sln
```

Check dependency references:

```bash
dotnet list src/Elsa.Platform.Deployment.Manifest/Elsa.Platform.Deployment.Manifest.csproj reference
```

Expected result:

- Focused tests pass.
- Full solution tests pass.
- Manifest package references Deployment Abstractions only.
- Existing repository warnings remain unchanged unless separately addressed.

## Deferred Follow-Up Slices

1. Folder and ZIP deployment artifact IO.
2. Manifest file loading from artifacts.
3. Deployment planner and validation pipeline.
4. CLI build/inspect/validate commands.
