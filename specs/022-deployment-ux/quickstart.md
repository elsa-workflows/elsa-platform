# Quickstart: Deployment UX

## Goal

Verify that deployment functionality is durable, workspace-scoped, and usable through the console without seeded cockpit data.

## API Smoke Scenario

1. Start the Platform API with customer workspace identity enabled.
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
dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj
dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceDeployment
cd src/Elsa.Platform.Console && npm test -- --run deployments
cd src/Elsa.Platform.Console && npm run typecheck
```
