# Quickstart: Elsa Package Catalog Admin Dashboard UI

This quickstart describes how to verify the planned admin dashboard once tasks
are implemented.

## Prerequisites

- .NET 10 SDK from `global.json`.
- Node.js compatible with the frontend toolchain selected during implementation.
- Local catalog database configured through the existing API development setup.

## Run the Catalog API

From the repository root:

```bash
dotnet run --project src/Elsa.Platform.PackageCatalog.Api
```

Use the existing admin API key configuration for authenticated admin requests.

## Run the Admin UI

From the admin UI project:

```bash
cd src/Elsa.Platform.PackageCatalog.AdminUi
npm install
npm run dev
```

Configure the UI to call the local Catalog API base URL and include the admin
API key according to the implementation's development configuration.

## Smoke Test the Four MVP Destinations

1. Open `/admin/overview`.
2. Confirm only Overview, Sources, Packages, and Sync Runs are present in primary
   navigation.
3. Confirm no Settings route is present.

## Verify Source Management

1. Open Sources.
2. Create a source using include pattern `Elsa.*` and exclude pattern `*.Tests`.
3. In the pattern tester, confirm:
   - `Elsa.Persistence.PostgreSql` is included.
   - `Elsa.Messaging.RabbitMQ` is included.
   - `Elsa.Tests` is excluded.
4. Save the source.
5. Disable and re-enable it.
6. Trigger Sync Now.
7. Soft-delete it and confirm it leaves the default active source list without
   claiming historical records were erased.

## Verify Package Review

1. Seed or sync packages with pending, approved, rejected, invalid, suspicious,
   and unlisted package versions.
2. Open Packages.
3. Search by package ID.
4. Filter to Pending.
5. Open a package version detail page.
6. Confirm approval controls target the selected package version only.
7. Reject a version and confirm a reason is required.
8. Approve a version and confirm validation/listing/suspicious states still
   influence public visibility explanation.

## Verify Validation and Manifest Inspection

1. Open a package version with validation errors or warnings.
2. Confirm Validation shows severity, code or rule ID, message, and path when
   available.
3. Open Manifest Viewer.
4. Confirm formatted JSON is shown when valid and raw inspection remains
   available.
5. Confirm there is no edit affordance for raw manifest content.

## Verify Sync Run Troubleshooting

1. Trigger a source sync.
2. Open Sync Runs.
3. Open the latest run.
4. Confirm trigger, status, started/completed times, summary counters, item
   diagnostics, failures, and warnings are visible.
5. Confirm active runs update by polling or manual refresh, not live streaming.

## Verify Error States

1. Use an invalid admin credential and confirm protected data is not shown as
   current after unauthorized responses.
2. Simulate a source validation error and confirm entered form values remain.
3. Simulate a partial bulk action failure and confirm item-level results are
   shown.
4. Simulate a refresh failure after data loaded and confirm stale data is labeled.

## Verification Commands

Expected implementation commands:

```bash
dotnet test
cd src/Elsa.Platform.PackageCatalog.AdminUi
npm test
npm run build
npm run e2e
```
