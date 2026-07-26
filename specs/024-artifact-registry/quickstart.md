# Quickstart: Deployment Artifact Registry

## Goal

Verify that workspace users can register artifact metadata, inspect artifact records, refresh safe inspection state, and use a real Artifacts console view without storing artifact payloads in the catalog database.

## API Smoke Scenario

1. Start the Valence Control API with customer workspace identity enabled.
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

## Follow-up Upload Smoke Scenario

This scenario belongs to the future upload slice defined in the PRD amendment.

1. Open `/admin/artifacts` as a setup-authorized workspace member.
2. Choose Upload artifact.
3. Select or drop a valid ZIP deployment artifact.
4. Confirm byte upload progress appears separately from server-side processing.
5. Complete the upload and verify the backend computes digest, manifest summary, resource summary, checksum status, and safe diagnostics.
6. Confirm the UI navigates to the created artifact detail page.
7. Upload the same ZIP again and verify duplicate/idempotent behavior.
8. Upload invalid, oversized, unsafe-path, unsupported-layout, and interrupted files and verify safe diagnostics with no deployable artifact record.

## Verification Commands

```sh
dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --filter WorkspaceArtifact
dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact
dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter WorkspaceArtifact
cd src/ValenceControl.Console && npm test -- --run artifacts
cd src/ValenceControl.Console && npm run typecheck
git diff --check
```

## Verification Results

- 2026-05-28: `dotnet test tests/ValenceControl.Deployment.Core.Tests/ValenceControl.Deployment.Core.Tests.csproj --filter WorkspaceArtifact` passed: 9 tests.
- 2026-05-28: `dotnet test tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests/ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests.csproj --filter DeploymentWorkspaceArtifact` passed: 6 tests.
- 2026-05-28: `dotnet test tests/ValenceControl.Api.Tests/ValenceControl.Api.Tests.csproj --filter WorkspaceArtifact` passed: 6 tests.
- 2026-05-28: `cd src/ValenceControl.Console && npm test -- --run artifacts` passed: 3 tests.
- 2026-05-28: `cd src/ValenceControl.Console && npm run typecheck` passed.
- 2026-05-28: `git diff --check` passed.

## Known Scope Boundaries

- Artifact upload and payload storage are out of the completed metadata registry slice, but are now specified as the next artifact-ingestion slice.
- OCI, signing, GitOps, provider apply, live runtime drift, and external approval workflows are out of scope.
- Production upload requires a configured artifact blob storage provider. The catalog database remains metadata-only.
- The first inspection refresh adapter may support local/test references only.
- Current implementation includes focused console, core, persistence, and API coverage for the metadata registry and local/test refresh adapter.
