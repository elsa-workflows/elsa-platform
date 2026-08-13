# Quickstart: Studio Submit To Valence Control

## Smoke Scenario

1. Install or enable the Studio Valence Control integration package.
2. Configure Valence Control endpoint, workspace, and authentication.
3. Open a workflow definition in Studio.
4. Choose **Submit to Valence Control**.
5. Verify Valence Control stores an `elsa.workflow-definition` artifact with safe metadata and a digest.
6. Verify no deployment run is created and the workflow is not made executable by submission alone.
7. Submit the same snapshot again and verify idempotent success.
8. Submit with unsafe metadata or invalid credentials and verify safe failure states.

## Verification Commands

```sh
dotnet test tests/ValenceControl.Studio.Submit.Tests/ValenceControl.Studio.Submit.Tests.csproj
dotnet test --filter StudioSubmit
dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter WorkspaceArtifact
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 1 test. This verifies the existing workspace artifact API accepts a Studio-shaped `elsa.workflow-definition` envelope, duplicate submission is idempotent, and artifact submission does not create deployment run history.
- 2026-05-29: `dotnet test tests/ValenceControl.Studio.Submit.Tests/ValenceControl.Studio.Submit.Tests.csproj --no-restore` passed: 13 tests. This verifies the Studio submit package contracts, configuration validation, safe result states, deterministic workflow snapshot packaging, unsafe metadata rejection, duplicate result handling, long workflow identity uniqueness, and safe unavailable-Valence Control messaging.
- 2026-05-29: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 1 test.
- 2026-05-29: `git diff --check` passed.
- 2026-05-29: `dotnet build ValenceControl.sln --no-restore` passed with existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-29: `dotnet test tests/ValenceControl.Studio.Submit.Tests/ValenceControl.Studio.Submit.Tests.csproj --no-restore` passed: 21 tests. This verifies the concrete Studio Valence Control HTTP client posts envelope metadata only, maps success/duplicate/error responses to safe submit states, handles unreadable Valence Control responses safely, and keeps raw workflow JSON out of the artifact registration request.
- 2026-05-29: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 2 tests. This verifies the Studio submit client registers and idempotently deduplicates a workflow artifact through the real Valence Control artifact API without creating deployment run history.

## Known Scope Boundaries

- Runtime application of submitted artifacts is covered by `029-workflow-artifact-runtime-applier`.
- Promotion and rollback of submitted artifacts are covered by `030-artifact-backed-promotion`.
