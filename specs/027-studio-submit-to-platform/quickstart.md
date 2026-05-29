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
dotnet test tests/Elsa.Platform.Studio.Submit.Tests/Elsa.Platform.Studio.Submit.Tests.csproj
dotnet test --filter StudioSubmit
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceArtifact
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 1 test. This verifies the existing workspace artifact API accepts a Studio-shaped `elsa.workflow-definition` envelope, duplicate submission is idempotent, and artifact submission does not create deployment run history.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Studio.Submit.Tests/Elsa.Platform.Studio.Submit.Tests.csproj --no-restore` passed: 10 tests. This verifies the Studio submit package contracts, configuration validation, safe result states, deterministic workflow snapshot packaging, unsafe metadata rejection, duplicate result handling, and safe unavailable-Platform messaging.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 1 test.
- 2026-05-29: `git diff --check` passed.
- 2026-05-29: `dotnet build Elsa.Platform.sln --no-restore` passed with existing `Microsoft.Build.Utilities.Core` NU1903 warnings.

## Known Scope Boundaries

- Runtime application of submitted artifacts is covered by `029-workflow-artifact-runtime-applier`.
- Promotion and rollback of submitted artifacts are covered by `030-artifact-backed-promotion`.
