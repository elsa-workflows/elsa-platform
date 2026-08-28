# Quickstart: Studio Submit To Elsa Control

## Smoke Scenario

1. Install or enable the Studio Elsa Control integration package.
2. Configure Elsa Control endpoint, workspace, and authentication.
3. Open a workflow definition in Studio.
4. Choose **Submit to Elsa Control**.
5. Verify Elsa Control stores an `elsa.workflow-definition` artifact with safe metadata and a digest.
6. Verify no deployment run is created and the workflow is not made executable by submission alone.
7. Submit the same snapshot again and verify idempotent success.
8. Submit with unsafe metadata or invalid credentials and verify safe failure states.

## Verification Commands

```sh
dotnet test tests/ElsaControl.Studio.Submit.Tests/ElsaControl.Studio.Submit.Tests.csproj
dotnet test --filter StudioSubmit
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceArtifact
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 1 test. This verifies the existing workspace artifact API accepts a Studio-shaped `elsa.workflow-definition` envelope, duplicate submission is idempotent, and artifact submission does not create deployment run history.
- 2026-05-29: `dotnet test tests/ElsaControl.Studio.Submit.Tests/ElsaControl.Studio.Submit.Tests.csproj --no-restore` passed: 13 tests. This verifies the Studio submit package contracts, configuration validation, safe result states, deterministic workflow snapshot packaging, unsafe metadata rejection, duplicate result handling, long workflow identity uniqueness, and safe unavailable-Elsa Control messaging.
- 2026-05-29: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 1 test.
- 2026-05-29: `git diff --check` passed.
- 2026-05-29: `dotnet build ElsaControl.sln --no-restore` passed with existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-29: `dotnet test tests/ElsaControl.Studio.Submit.Tests/ElsaControl.Studio.Submit.Tests.csproj --no-restore` passed: 21 tests. This verifies the concrete Studio Elsa Control HTTP client posts envelope metadata only, maps success/duplicate/error responses to safe submit states, handles unreadable Elsa Control responses safely, and keeps raw workflow JSON out of the artifact registration request.
- 2026-05-29: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter Studio_submit --no-restore` passed: 2 tests. This verifies the Studio submit client registers and idempotently deduplicates a workflow artifact through the real Elsa Control artifact API without creating deployment run history.

## Known Scope Boundaries

- Runtime application of submitted artifacts is covered by `029-workflow-artifact-runtime-applier`.
- Promotion and rollback of submitted artifacts are covered by `030-artifact-backed-promotion`.
