# Quickstart: Custom Deployment Tiers

## Goal

Verify that workspaces can replace fixed Dev/Test/Stage/Production environment tiers with custom tier definitions composed from stable coded capabilities, while preserving existing deployment environment behavior.

## API Smoke Scenario

1. Start the Elsa Control API with customer workspace identity enabled.
2. Sign in as a workspace owner.
3. Request the capability catalog:

   ```http
   GET /api/workspaces/{workspaceId}/deployments/tier-capabilities
   ```

4. Confirm stable capability IDs are returned, including production-like, promotion source/target, confirmation required, rollback enabled, secret verification required, and observability required.
5. Request workspace tiers:

   ```http
   GET /api/workspaces/{workspaceId}/deployments/tiers
   ```

6. Confirm default Dev, Test, Stage, and Production tier definitions exist when the workspace has no custom configuration.
7. Create a custom tier:

   ```http
   POST /api/workspaces/{workspaceId}/deployments/tiers
   ```

   with name `Production EU` and production-like capabilities.

8. Create or update a deployment environment using the custom tier ID.
9. Reload the deployment cockpit and confirm the environment shows the custom tier label and capability IDs.
10. Create another custom tier named `Production US` with the same capabilities and confirm deployment safeguards match `Production EU`.
11. Attempt to create a duplicate active tier name and confirm the request is rejected.
12. Archive a tier and confirm it is not available for new environment assignments while existing environments remain readable.
13. Attempt to use a tier ID from another workspace and confirm the request is rejected.

## Console Smoke Scenario

1. Open `/admin/deployments` as a workspace owner.
2. Open deployment tier management.
3. Confirm default tiers are visible.
4. Create a tier named `UAT` with pre-production and promotion capabilities.
5. Edit the `UAT` tier and change its capability set.
6. Confirm impact preview appears if environments use the tier.
7. Create a deployment environment and select `UAT`.
8. Confirm the cockpit displays the environment with the `UAT` tier label.
9. Sign in as a non-admin workspace member with deployment read permission.
10. Confirm tier labels are visible but tier mutation controls are unavailable.

## Verification Commands

```sh
dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --filter DeploymentTier
dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceTier
dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceDeploymentTier
cd src/ElsaControl.Console && npm test -- --run deployments
cd src/ElsaControl.Console && npm run typecheck
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/ElsaControl.Deployment.Core.Tests/ElsaControl.Deployment.Core.Tests.csproj --filter DeploymentTier` passed: 8 tests.
- 2026-05-28: `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceTier` passed: 10 tests.
- 2026-05-28: `dotnet test tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter "DeploymentWorkspacePersistenceTests|DeploymentWorkspaceTier"` passed: 20 tests.
- 2026-05-28: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter WorkspaceDeploymentTier` passed: 7 tests.
- 2026-05-28: `dotnet test tests/ElsaControl.Api.Tests/ElsaControl.Api.Tests.csproj --filter "WorkspaceDeploymentApiTests|WorkspaceDeploymentTier"` passed: 16 tests.
- 2026-05-28: `cd src/ElsaControl.Console && npm test -- --run deployments` passed: 22 tests.
- 2026-05-28: `cd src/ElsaControl.Console && npm run typecheck` passed.
- 2026-05-28: `git diff --check` passed.

## Known Scope Boundaries

- Tier capabilities are platform-defined and cannot be created by workspace admins.
- Custom tiers are workspace-scoped and are not shared across workspaces in this feature.
- External approval systems and advanced policy authoring are out of scope.
- Runtime instance state, logs, bookmarks, queues, and telemetry ingestion remain outside this control-plane feature.
