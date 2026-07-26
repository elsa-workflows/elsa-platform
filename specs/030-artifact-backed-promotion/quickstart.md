# Quickstart: Artifact Backed Promotion

## Smoke Scenario

1. Register or submit a workflow artifact.
2. Create a desired-state revision that references the artifact.
3. Preview promotion from source to target environment.
4. Confirm promotion and create the target artifact-backed revision.
5. Queue deployment and verify the runtime command includes the artifact reference.
6. Deploy a second revision and roll back to the first.
7. Verify rollback queues the known-good artifact reference.

## Verification Commands

```sh
dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --filter ArtifactBackedPromotion
dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter ArtifactBackedPromotion
dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter ArtifactBackedPromotion
cd src/ValenceControl.Console && npm test -- --run deployments
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentCommand --no-restore` passed: 7 tests, including artifact-backed command reference projection and missing artifact rejection.
- 2026-05-28: `dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --filter DeploymentValidation --no-restore` passed: 5 tests.
- 2026-05-29: `dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --no-restore -v minimal` passed: 62 tests, including artifact-backed promotion preview output for safe metadata, configuration, and runtime compatibility.
- 2026-05-29: `dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --no-restore -v minimal` passed: 69 tests, including structured artifact-reference projection and legacy record readability.
- 2026-05-29: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --no-restore -v minimal --filter Owner_can_create_desired_state_revision_and_preview_promotion` passed: 1 test.
- 2026-05-29: SQLite and SQL Server `dotnet ef migrations script` checks passed for `AddArtifactDesiredStateReferences`, including JSON backfill SQL.
- 2026-05-29: `dotnet build ValenceControl.sln --no-restore` passed with the existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-29: `git diff --check` passed.
- 2026-05-30: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --no-restore -v minimal --filter "WorkspaceDeploymentApiTests|RuntimeCommandApiTests"` passed: 17 tests, including artifact promotion, safe command creation, digest mismatch rejection, and default runtime capability fallback.
- 2026-05-30: `dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --no-restore -v minimal` passed: 63 tests, including promotion preview engine/environment mismatch validation.
- 2026-05-30: `dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --no-restore -v minimal --filter DeploymentCommand` passed: 15 tests.
- 2026-05-30: `dotnet build ValenceControl.sln --no-restore -v minimal` passed with the existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-30: `git diff --check` passed.
- 2026-05-30: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --no-restore -v minimal --filter "WorkspaceDeploymentApiTests|RuntimeCommandApiTests"` passed: 20 tests, including artifact-backed rollback success and missing rollback artifact rejection.
- 2026-05-30: `dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --no-restore -v minimal` passed: 63 tests.
- 2026-05-30: `dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --no-restore -v minimal --filter DeploymentCommand` passed: 15 tests.
- 2026-05-30: `cd src/ValenceControl.Console && npm test -- --run deployments` passed: 22 tests, including artifact metadata in promotion previews and run command summaries.
- 2026-05-30: `cd src/ValenceControl.Console && npm run typecheck` passed.
- 2026-05-30: `dotnet build ValenceControl.sln --no-restore -v minimal` passed with the existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-30: `git diff --check` passed.

## Known Scope Boundaries

- Studio artifact submission is covered by `027-studio-submit-to-valence-control`.
- Runtime artifact application is covered by `029-workflow-artifact-runtime-applier`.
