# Quickstart: Runtime Command Sync

## API Smoke Scenario

1. Start the Platform API with workspace identity enabled.
2. Sign in as a workspace owner and create a workflow application, environment, engine, artifact, and deployable run.
3. Confirm a pending deployment command is created for the target engine.
4. Poll runtime commands for the target engine.
5. Claim the command with worker ID `runtime-sync-01`.
6. Attempt to claim the same command from worker ID `runtime-sync-02`.
7. Confirm the second claim is rejected while the first lease is active.
8. Post heartbeat and progress for the first lease.
9. Complete the command with observed artifact digest and runtime reference.
10. Reload deployment run detail and confirm command events appear in history.
11. Repeat completion with the same lease and verify the final state is idempotent.
12. Claim another command, let the lease become stale, run recovery, and verify the command is recovery-required or explicitly reclaimable without duplicate apply.

## Webhook Trigger Scenario

1. Enable webhook-triggered fetch for a runtime registration.
2. Queue a deployment command.
3. Confirm a webhook notification record/event is emitted with safe command hint metadata.
4. Deliver the notification twice.
5. Confirm the runtime still must poll/claim and no duplicate apply occurs.
6. Disable webhook delivery and confirm polling still discovers the pending command.

## Verification Commands

```sh
dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter DeploymentCommand
dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentCommand
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand
cd src/Elsa.Platform.Console && npm test -- --run deployments
cd src/Elsa.Platform.Console && npm run typecheck
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter DeploymentCommand --no-restore` passed: 8 tests.
- 2026-05-28: `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentCommand --no-restore` passed: 5 tests.
- 2026-05-28: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand --no-restore` passed: 3 tests.
- 2026-05-28: `cd src/Elsa.Platform.Console && npm test -- --run deployments` passed: 22 tests.
- 2026-05-28: `cd src/Elsa.Platform.Console && npm run typecheck` passed.
- 2026-05-28: `git diff --check` passed.
- 2026-05-29: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter RuntimeCommand --no-restore` passed: 4 tests.
- 2026-05-29: `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentCommand --no-restore` passed: 14 tests.

## Known Scope Boundaries

- Runtime package implementation is covered by `029-workflow-artifact-runtime-applier`.
- Studio artifact submission is covered by `027-studio-submit-to-platform`.
- Provider-specific webhook senders, direct push providers, and runtime credential packages can follow after the core command contract.
- Artifact-backed promotion and rollback are covered by `030-artifact-backed-promotion`.
