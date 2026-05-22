# Quickstart: Package Details Page

## Local Verification

1. Start the API host with admin authentication configured.
2. Seed or create one package with:
   - At least two indexed versions.
   - One latest indexed version that is pending, invalid, unlisted, or
     suspicious.
   - Feature records with settings.
   - Dependency, conflict, compatibility, infrastructure, and validation data
     where available.
   - Stored manifest JSON and manifest hash.
3. Open `GET /api/admin/packages/<packageId>` with admin credentials.
4. Confirm the response returns canonical package casing, source summary, all
   indexed versions, per-version statuses, visibility reasons, features,
   settings, and manifest metadata.
5. Repeat the request with different package ID casing and confirm it resolves
   to the same package while displaying canonical casing.
6. Open `GET /api/admin/packages/<packageId>/versions/<version>/validation`.
7. Confirm validation errors and warnings are normalized into findings with
   severity, message, optional code/path, and blocking impact.
8. Approve a selected version with
   `POST /api/admin/packages/<packageId>/versions/<version>/approve` and a body
   containing the selected version's `expectedStateToken`:
   `{"expectedStateToken":"<versionStateToken>","reason":"Reviewed"}`.
9. Reject a selected version with a non-empty reason and the selected version's
   `expectedStateToken` using
   `POST /api/admin/packages/<packageId>/versions/<version>/reject`:
   `{"expectedStateToken":"<versionStateToken>","reason":"Not ready"}`.
10. Attempt a rejection with a blank reason and confirm it is blocked.

## Console Verification

1. Open `/admin/packages`.
2. Select a package link and confirm `/admin/packages/:packageId` opens the
   Package Details page.
3. Confirm the latest indexed version is selected by default.
4. Switch versions and verify summary, visibility reasons, validation findings,
   features, manifest content, and available actions update together.
5. Open `/admin/packages/:packageId/versions/:version/validation` and confirm the
   selected version and validation section are restored from the route.
6. Search/filter validation findings, feature rows, setting rows, dependencies,
   conflicts, and manifest content.
7. Confirm a hidden version lists all known visibility blockers.
8. Confirm missing package, missing version, missing manifest, validation-load
   failure, access-denied, and stale-action states are visible and scoped.
9. Confirm approval and rejection confirmations identify the exact package ID and
   selected version, with rejection requiring a reason.

## Automated Verification

Run:

```sh
dotnet test tests/Elsa.Platform.PackageCatalog.Api.Tests/Elsa.Platform.PackageCatalog.Api.Tests.csproj --filter AdminPackages
dotnet test tests/Elsa.Platform.PackageCatalog.Api.Tests/Elsa.Platform.PackageCatalog.Api.Tests.csproj --filter AdminValidation
dotnet test tests/Elsa.Platform.PackageCatalog.Api.Tests/Elsa.Platform.PackageCatalog.Api.Tests.csproj --filter AdminApproval
cd src/Elsa.Platform.Console && npm test -- src/features/packages/PackageDetailsPage.test.tsx src/features/packages/packageModels.test.ts
cd tests/Elsa.Platform.Console.E2E && npm run e2e -- package-details.spec.ts
```

## Implementation Verification Notes

Verified on 2026-05-17:

- `dotnet test tests/Elsa.Platform.PackageCatalog.Api.Tests/Elsa.Platform.PackageCatalog.Api.Tests.csproj`
- `cd src/Elsa.Platform.Console && npm test`
- `cd src/Elsa.Platform.Console && npm run typecheck`
- `cd src/Elsa.Platform.Console && npm run build`
- `cd tests/Elsa.Platform.Console.E2E && npm run e2e -- package-details.spec.ts`

The implementation uses the existing catalog database only. It adds no public
catalog API behavior, does not change the manifest schema, reads stored manifest
JSON as data, and does not execute package code.
