# Quickstart: Workflow Artifact Runtime Applier

## Smoke Scenario

1. Install or enable the Elsa Workflows Platform runtime integration.
2. Register runtime capabilities for `elsa.workflow-definition`.
3. Queue a Platform deployment command for a workflow artifact.
4. Run the runtime sync worker.
5. Verify the worker claims the command, fetches the payload, verifies digest, applies the workflow definition, and completes the command.
6. Verify duplicate delivery does not duplicate the local workflow definition.
7. Verify invalid digest, unsupported schema, and local validation failure report safe diagnostics.

## Verification Commands

```sh
dotnet test tests/Elsa.Platform.Workflows.RuntimeApplier.Tests/Elsa.Platform.Workflows.RuntimeApplier.Tests.csproj
dotnet test --filter WorkflowArtifactRuntimeApplier
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand
git diff --check
```

## Verification Results

- 2026-05-29: `dotnet test tests/Elsa.Platform.Workflows.RuntimeApplier.Tests/Elsa.Platform.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 11 tests. This verifies the runtime applier package contracts, workflow artifact capability advertisement, payload digest validation, unsupported schema and missing capability rejection, JSON object payload validation, safe diagnostics, and apply result success semantics.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand --no-restore` passed: 5 tests.
- 2026-05-29: `dotnet build Elsa.Platform.sln --no-restore` passed with existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-29: `git diff --check` passed.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Workflows.RuntimeApplier.Tests/Elsa.Platform.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 22 tests. This adds runtime command HTTP client coverage for polling, claiming, conflict handling, progress, completion, failure, rejection, safe diagnostics, malformed success responses, lease duration validation, and forward-compatible unknown command states.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter Runtime_applier_client --no-restore` passed: 1 test. This verifies the runtime applier package client can poll, claim, progress, and complete through the real Platform runtime command API.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Workflows.RuntimeApplier.Tests/Elsa.Platform.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 34 tests. This adds runtime lease evaluation, safety-margin handling, heartbeat due detection, and bounded retry decision coverage.

## Known Scope Boundaries

- Studio artifact production is covered by `027-studio-submit-to-platform`.
- Artifact-backed promotion and rollback are covered by `030-artifact-backed-promotion`.
