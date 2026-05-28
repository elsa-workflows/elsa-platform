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
dotnet test --filter WorkflowArtifactRuntimeApplier
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand
git diff --check
```

## Verification Results

- Not run yet. This spec defines a future implementation slice.

## Known Scope Boundaries

- Studio artifact production is covered by `027-studio-submit-to-platform`.
- Artifact-backed promotion and rollback are covered by `030-artifact-backed-promotion`.
