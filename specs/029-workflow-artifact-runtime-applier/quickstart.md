# Quickstart: Workflow Artifact Runtime Applier

## Smoke Scenario

1. Install or enable the Elsa Control runtime integration for Elsa Workflows.
2. Register runtime capabilities for `elsa.workflow-definition`.
3. Queue a Elsa Control deployment command for a workflow artifact.
4. Run the runtime sync worker.
5. Verify the worker claims the command, fetches the payload, verifies digest, applies the workflow definition, and completes the command.
6. Verify duplicate delivery does not duplicate the local workflow definition.
7. Verify invalid digest, unsupported schema, and local validation failure report safe diagnostics.

## Verification Commands

```sh
dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj
dotnet test --filter WorkflowArtifactRuntimeApplier
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter RuntimeCommand
git diff --check
```

## Verification Results

- 2026-05-29: `dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 11 tests. This verifies the runtime applier package contracts, workflow artifact capability advertisement, payload digest validation, unsupported schema and missing capability rejection, JSON object payload validation, safe diagnostics, and apply result success semantics.
- 2026-05-29: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter RuntimeCommand --no-restore` passed: 5 tests.
- 2026-05-29: `dotnet build ElsaControl.sln --no-restore` passed with existing `Microsoft.Build.Utilities.Core` NU1903 warnings.
- 2026-05-29: `git diff --check` passed.
- 2026-05-29: `dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 22 tests. This adds runtime command HTTP client coverage for polling, claiming, conflict handling, progress, completion, failure, rejection, safe diagnostics, malformed success responses, lease duration validation, and forward-compatible unknown command states.
- 2026-05-29: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter Runtime_applier_client --no-restore` passed: 1 test. This verifies the runtime applier package client can poll, claim, progress, and complete through the real Elsa Control runtime command API.
- 2026-05-29: `dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 39 tests. This adds runtime lease evaluation, ownership proof, safety-margin handling, heartbeat due detection, and bounded retry decision coverage.
- 2026-05-29: `dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 81 tests. This adds HTTP(S) workflow artifact payload loading with provider/host approval, private-address blocking, pinned transport connections, proxy bypass, redirect rejection, expired-reference, URI/scheme, concrete media-type, response content-type fallback, size-limit, timeout, remote failure, caller-owned transport disposal, and safe error handling coverage.
- 2026-05-29: `dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 93 tests. This completes runtime digest and schema compatibility validation, including case-insensitive SHA-256 digest comparison, payload reference digest consistency, runtime version range compatibility, required runtime version advertisement, prerelease and malformed range rejection, unsupported schema rejection, missing capability rejection, invalid JSON payload rejection, and safe diagnostics.
- 2026-05-29: `dotnet test tests/ElsaControl.Workflows.RuntimeApplier.Tests/ElsaControl.Workflows.RuntimeApplier.Tests.csproj --no-restore -v minimal` passed: 103 tests. This completes the workflow artifact command processor slice with Elsa Control artifact envelope lookup, local workflow definition apply adapter, apply journal/idempotency guard, successful apply completion, duplicate-delivery no-op apply, digest mismatch rejection, local validation rejection, unexpected apply failure reporting, runtime reference reporting, workspace-scoped artifact response checks, and safe diagnostics.
- 2026-05-29: `git diff --check` passed.
- 2026-05-29: `dotnet build ElsaControl.sln --no-restore` passed with existing `Microsoft.Build.Utilities.Core` NU1903 warnings.

## Known Scope Boundaries

- Studio artifact production is covered by `027-studio-submit-to-elsa-control`.
- Artifact-backed promotion and rollback are covered by `030-artifact-backed-promotion`.
