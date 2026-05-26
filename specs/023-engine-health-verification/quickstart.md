# Quickstart: Engine Health Verification

## Goal

Verify that workspace-owned workflow engine registrations can move from unreachable/unverified state to persisted healthy, degraded, or unreachable health metadata through manual verification and heartbeat updates.

## API Smoke Scenario

1. Start the Platform API with customer workspace identity enabled.
2. Sign in as a customer workspace owner.
3. Create a workflow application, environment, and engine registration.
4. Confirm the engine starts as `Unreachable` with no heartbeat.
5. Call manual verification:

   ```http
   POST /api/workspaces/{workspaceId}/deployments/engines/{engineId}/verify
   ```

6. Reload cockpit and confirm health, version, certificate status, credential verification status, last verification, last heartbeat, and safe message are updated.
7. Submit a heartbeat:

   ```http
   POST /api/workspaces/{workspaceId}/deployments/engines/{engineId}/heartbeat
   ```

8. Reload cockpit and confirm only that engine's metadata changed.
9. Submit a stale heartbeat and verify it is rejected without overwriting newer metadata.
10. Attempt verification and heartbeat updates against another workspace's engine and verify the request is rejected.
11. Confirm runtime controls remain blocked while the engine is unreachable and become eligible only when health, permission, capability, and confirmation gates pass.

## Console Smoke Scenario

1. Open `/admin/deployments` as a customer workspace member.
2. Register an engine or select an existing unreachable engine.
3. Open Engine Registration.
4. Confirm last heartbeat and last verification explain why the engine is not yet verified.
5. Click Verify.
6. Confirm pending state appears.
7. Confirm cockpit refreshes after verification.
8. Confirm safe diagnostic text appears for failed/degraded verification.
9. Confirm supported runtime controls are disabled for unreachable engines and available only after successful health verification plus existing permission/capability gates.

## Verification Commands

```sh
dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter EngineHealth
dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspacePersistenceTests
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceDeploymentEngineHealth
cd src/Elsa.Platform.Console && npm test -- --run deployments
cd src/Elsa.Platform.Console && npm run typecheck
git diff --check
```

## Verification Results

- `dotnet build Elsa.Platform.sln --no-restore`: blocked in this Codex desktop shell because `dotnet` is not on `PATH`.
- `dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter EngineHealth`: not run because `dotnet` is not on `PATH`.
- `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspacePersistenceTests`: not run because `dotnet` is not on `PATH`.
- `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceDeploymentEngineHealth`: not run because `dotnet` is not on `PATH`.
- `PATH="/Users/sipke/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH" ./node_modules/.bin/vitest run deployments`: passed, 14 tests.
- `PATH="/Applications/Codex.app/Contents/Resources:$PATH" ./node_modules/.bin/tsc -b`: passed.
- `git diff --check`: passed.

## Known Scope Boundaries

- Verification stores control-plane metadata only.
- Production engine API probing can evolve behind the verification abstraction.
- Live deployment apply, runtime instance inspection, live drift detection, and telemetry provider calls remain out of scope.
