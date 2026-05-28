# Quickstart: Studio Submit To Platform

## Smoke Scenario

1. Install or enable the Studio Platform integration package.
2. Configure Platform endpoint, workspace, and authentication.
3. Open a workflow definition in Studio.
4. Choose **Submit to Platform**.
5. Verify Platform stores an `elsa.workflow-definition` artifact with safe metadata and a digest.
6. Verify no deployment run is created and the workflow is not made executable by submission alone.
7. Submit the same snapshot again and verify idempotent success.
8. Submit with unsafe metadata or invalid credentials and verify safe failure states.

## Verification Commands

```sh
dotnet test --filter StudioSubmit
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceArtifact
git diff --check
```

## Verification Results

- Not run yet. This spec defines a future implementation slice.

## Known Scope Boundaries

- Runtime application of submitted artifacts is covered by `029-workflow-artifact-runtime-applier`.
- Promotion and rollback of submitted artifacts are covered by `030-artifact-backed-promotion`.
