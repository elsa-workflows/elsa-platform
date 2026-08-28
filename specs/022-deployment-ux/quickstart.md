# Quickstart: Deployment UX

## Goal

Verify that deployment functionality is durable, workspace-scoped, and usable through the console without seeded cockpit data.

## API Smoke Scenario

1. Start the Elsa Control API with customer workspace identity enabled.
2. Sign in as a customer workspace owner.
3. Call `GET /api/me/workspaces` and record the workspace ID.
4. Confirm the caller has effective deployment permissions:

   ```http
   GET /api/workspaces/{workspaceId}/deployments/permissions
   ```

5. Create a workflow application:

   ```http
   POST /api/workspaces/{workspaceId}/deployments/applications
   ```

6. Create `Dev` and `Prod` environments for the application.
7. Register an engine for `Prod` using a provider-backed credential reference.
8. Create structured desired-state revisions for `Dev` and `Prod`.
9. Call promotion preview from `Dev` to `Prod`.
10. Confirm validation blockers prevent deployment when required secrets or capabilities are missing.
11. Add the missing reference/capability and preview again.
12. Create an explicit confirmation for deployment.
13. Enqueue a deployment run.
14. Reload cockpit and confirm the queued/running/completed run appears in history.
15. Create an explicit confirmation for rollback.
16. Start rollback from the previous successful run and confirm a new queued rollback run appears.
17. Simulate process restart with a queued run and verify the run remains processable.
18. Simulate process restart with a stale claimed run and verify it moves to `RecoveryRequired` without automatic replay.
19. Seed the normal cockpit dataset size and verify cockpit load stays under 3 seconds with bounded database query count.

## Isolation Smoke Scenario

1. Seed two workspaces with different applications and engines.
2. Sign in as a member of workspace A.
3. Request workspace A cockpit and verify records are returned.
4. Request workspace B cockpit and verify the request is rejected.
5. Attempt to submit workspace B IDs to workspace A deployment routes and verify the request is rejected.

## Console Smoke Scenario

1. Open `/admin/deployments` as a customer workspace member.
2. Confirm no seeded sample values appear in an empty workspace.
3. Create a workflow application.
4. Create an environment.
5. Register an engine.
6. Open the cockpit and confirm the new records render.
7. Preview a promotion and confirm diff/validation state renders.
8. Confirm and start a queued deployment run when validation has no blockers.
9. Refresh the page and confirm run history persists.
10. Confirm and execute a supported runtime control and confirm unsupported controls are absent or disabled.
11. Confirm observability/drift views render persisted metadata without live provider calls.
12. Attempt to reuse, expire, or consume another user's confirmation and verify deployment, rollback, and runtime control actions are rejected.

## Verification Commands

```sh
dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj
dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceDeployment
cd src/ElsaControl.Console && npm test -- --run deployments
cd src/ElsaControl.Console && npm run typecheck
cd tests/ElsaControl.Console.E2E && ADMIN_UI_BASE_URL=http://127.0.0.1:5173 npm run e2e -- deployments.spec.ts
```

## Current Implementation Notes

- Engine registrations start as `Unreachable` until a future engine heartbeat or verification endpoint updates health metadata.
- Observability and drift views render persisted metadata only; this slice does not call live telemetry providers or perform live drift detection.
- Deployment, rollback, and runtime controls create single-user confirmations immediately before queueing or execution.
- Deployment and rollback run history is projected from persisted run records into the cockpit response.

## Verification Results

Results are recorded during implementation and final verification.

| Command | Result | Notes |
| --- | --- | --- |
| `dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --no-restore` | Passed | 26 tests |
| `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --no-restore` | Passed | 31 tests |
| `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --no-restore --filter WorkspaceDeployment` | Passed | 19 tests |
| `cd src/ElsaControl.Console && npm test -- --run deployments` | Passed | 13 tests |
| `cd src/ElsaControl.Console && npm run typecheck` | Passed | TypeScript project build |
| `cd tests/ElsaControl.Console.E2E && ADMIN_UI_BASE_URL=http://127.0.0.1:5173 npm run e2e -- deployments.spec.ts` | Passed | 1 Chromium smoke test; requires console dev server |
| `dotnet test ElsaControl.sln --no-restore -m:1 /nr:false` | Passed | Existing `NU1903` warning for `Microsoft.Build.Utilities.Core` in package manifest generator projects |
| `git diff --check` | Passed | No whitespace errors |

## Safety Review Results

- Secret handling: cockpit and console render credential provider/reference metadata only; no raw secret values or provider tokens are returned by deployment responses.
- Permission checks: every deployment route resolves workspace access and checks the operation-specific deployment permission before mutation.
- Confirmation checks: deploy, rollback, and runtime control execution consume same-user single-use confirmations using action and target matching.
- Runtime control gating: controls require advertised matching capability and now fail closed on `Unreachable` engine health before confirmation is consumed.
- Observability/drift: cockpit renders persisted metadata only and does not call live telemetry providers or registered engines.
- Audit metadata: deployment runs, run history, and runtime control executions persist actor, target, confirmation, status, and timestamps.
