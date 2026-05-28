# Quickstart: Deployment Artifact Registry

## Goal

Verify that workspace users can register artifact metadata, inspect artifact records, refresh safe inspection state, and use a real Artifacts console view without storing artifact payloads in the catalog database.

## API Smoke Scenario

1. Start the Platform API with customer workspace identity enabled.
2. Sign in as a workspace owner.
3. Register artifact metadata:

   ```http
   POST /api/workspaces/{workspaceId}/artifacts
   ```

4. List artifacts:

   ```http
   GET /api/workspaces/{workspaceId}/artifacts
   ```

5. Open artifact detail:

   ```http
   GET /api/workspaces/{workspaceId}/artifacts/{artifactRecordId}
   ```

6. Refresh inspection:

   ```http
   POST /api/workspaces/{workspaceId}/artifacts/{artifactRecordId}/refresh
   ```

7. Verify duplicate registration is idempotent for identical metadata and conflicting for changed metadata.
8. Verify a reader without setup permission can read but cannot register or refresh.
9. Verify a caller cannot read or mutate another workspace's artifact records.
10. Verify no response contains payload content, manifest JSON, workflow definition content, tokens, passwords, or secret values.

## Console Smoke Scenario

1. Open `/admin/artifacts` as a workspace member.
2. Confirm an empty state appears when no artifacts exist.
3. Register artifact metadata as a setup-authorized user.
4. Confirm the artifact appears in the list and detail.
5. Refresh inspection and confirm state/diagnostics update.
6. Confirm registration and refresh are disabled for users without setup permission.

## Verification Commands

```sh
dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter WorkspaceArtifact
dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact
dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceArtifact
cd src/Elsa.Platform.Console && npm test -- --run artifacts
cd src/Elsa.Platform.Console && npm run typecheck
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/Elsa.Platform.Deployment.Core.Tests/Elsa.Platform.Deployment.Core.Tests.csproj --filter WorkspaceArtifact` passed: 9 tests.
- 2026-05-28: `dotnet test tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests/Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact` passed: 6 tests.
- 2026-05-28: `dotnet test tests/Elsa.Platform.Api.Tests/Elsa.Platform.Api.Tests.csproj --filter WorkspaceArtifact` passed: 6 tests.
- 2026-05-28: `cd src/Elsa.Platform.Console && npm test -- --run artifacts` passed: 3 tests.
- 2026-05-28: `cd src/Elsa.Platform.Console && npm run typecheck` passed.
- 2026-05-28: `git diff --check` passed.

## Known Scope Boundaries

- Artifact upload and payload storage are out of scope.
- OCI, signing, GitOps, provider apply, live runtime drift, object storage, and external approval workflows are out of scope.
- The first inspection refresh adapter may support local/test references only.
- Current implementation includes focused console, core, persistence, and API coverage for the metadata registry and local/test refresh adapter.
