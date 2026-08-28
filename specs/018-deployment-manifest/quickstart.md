# Quickstart: Deployment Manifest Parsing

## Goal

Verify that v1alpha manifests parse and normalize independently from artifact IO or engine execution.

## Commands

Run focused tests:

```bash
dotnet test tests/ElsaControl.Deployment.Manifest.Tests/ElsaControl.Deployment.Manifest.Tests.csproj
```

Run full solution tests:

```bash
dotnet test ElsaControl.sln
```

Check dependency references:

```bash
dotnet list src/ElsaControl.Deployment.Manifest/ElsaControl.Deployment.Manifest.csproj reference
```

Expected result:

- Focused tests pass.
- Full solution tests pass.
- Manifest package references Deployment Abstractions only.
- Existing repository warnings remain unchanged unless separately addressed.

## Verification Notes

Last verified on 2026-05-20:

```text
dotnet list src/ElsaControl.Deployment.Manifest/ElsaControl.Deployment.Manifest.csproj reference
Project reference: ../ElsaControl.Deployment.Abstractions/ElsaControl.Deployment.Abstractions.csproj
```

```text
dotnet test tests/ElsaControl.Deployment.Manifest.Tests/ElsaControl.Deployment.Manifest.Tests.csproj
Passed: 18
```

```text
dotnet test ElsaControl.sln
Passed all .NET test projects
```

Known pre-existing warning:

- `Microsoft.Build.Utilities.Core` 17.14.8 emits NU1903 for GHSA-w3q9-fxm7-j8fq in the package manifest generator MSBuild project and tests.

## Deferred Follow-Up Slices

1. Folder and ZIP deployment artifact IO.
2. Manifest file loading from artifacts.
3. Deployment planner and validation pipeline.
4. CLI build/inspect/validate commands.
