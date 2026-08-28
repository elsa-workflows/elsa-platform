# Quickstart: Runtime Kind Compatibility

## Validate manifest behavior

1. Add package-level runtime kind examples for `elsa.server` and `elsa.studio`.
2. Add a mixed package example with feature-level runtime-kind overrides.
3. Validate that official and custom runtime-kind values are accepted.
4. Validate that blank, whitespace, duplicate, and malformed values are rejected.
5. Validate that existing manifests without runtime kinds still resolve to Elsa Server compatibility.

## Verify catalog ingestion and projection

1. Sync or ingest a server-only manifest.
2. Sync or ingest a studio-only manifest.
3. Sync or ingest a mixed manifest with server and studio features.
4. Query package and feature projections.
5. Confirm effective runtime kinds are present and unknown valid values are preserved.

## Verify Runtime Builder behavior

1. Open or query the Elsa Server builder catalog.
2. Confirm server-compatible packages are available.
3. Confirm studio-only packages are excluded.
4. Confirm mixed packages expose only server-compatible features.
5. Confirm existing undeclared packages remain available.

## Suggested checks

```bash
dotnet test tests/Elsa.Specifications.PackageManifests.Tests/Elsa.Specifications.PackageManifests.Tests.csproj
dotnet test tests/ElsaControl.PackageCatalog.Core.Tests/ElsaControl.PackageCatalog.Core.Tests.csproj
dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj
dotnet test tests/ElsaControl.RuntimeBuilder.Core.Tests/ElsaControl.RuntimeBuilder.Core.Tests.csproj
git diff --check
```
