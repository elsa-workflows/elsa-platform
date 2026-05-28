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
dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter ArtifactBackedPromotion
dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter ArtifactBackedPromotion
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter ArtifactBackedPromotion
cd src/Elsa.Platform.Console && npm test -- --run deployments
git diff --check
```

## Verification Results

- Not run yet. This spec defines a future implementation slice.

## Known Scope Boundaries

- Studio artifact submission is covered by `027-studio-submit-to-platform`.
- Runtime artifact application is covered by `029-workflow-artifact-runtime-applier`.
