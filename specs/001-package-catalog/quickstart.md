# Quickstart: Valence Control Package Catalog

This quickstart describes how an implementer should validate the first usable
slice once tasks are generated and implemented.

## Prerequisites

- .NET 10 SDK.
- Local shell with `dotnet`.
- No external database is required for the first version; SQLite is used locally.

## Build

```bash
dotnet restore
dotnet build
```

## Test

```bash
dotnet test
```

Expected coverage areas:

- Manifest contract serialization and extension data preservation.
- JSON Schema validation for valid, invalid, oversized, and unsupported schema
  manifests.
- Public API filtering for valid, approved, listed records only.
- Admin API key enforcement.
- Sync behavior for valid, invalid, missing-manifest, unchanged, and suspicious
  package versions.
- Compatibility check pass, warning, and error outcomes.

## Run Locally

```bash
dotnet run --project src/ValenceControl.Api
```

Expected default behavior:

- API listens on the configured ASP.NET Core URLs.
- SQLite database is created or migrated locally.
- Admin endpoints require `X-Api-Key`.
- Scheduled sync is enabled only when configured sources exist.

## Seed A Source

Create a NuGet feed source:

```bash
curl -X POST http://localhost:5000/api/admin/sources \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  -d '{
    "name": "Local Elsa Packages",
    "type": "NuGetFeed",
    "url": "https://api.nuget.org/v3/index.json",
    "enabled": true,
    "includePatterns": ["Elsa.*"],
    "excludePatterns": ["Elsa.Experimental.*"],
    "approvalPolicy": "Manual",
    "versionDiscoveryPolicy": "LatestStable"
  }'
```

For a preview feed, use `LatestPreview` to select only prerelease versions whose
label is `preview` or starts with `preview`, case-insensitively. Use
`LatestIncludingPrerelease` only when release candidates and branch-named
prereleases are also intended candidates.

## Trigger Sync

```bash
curl -X POST http://localhost:5000/api/admin/sync \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  -d '{"forceReindex": false}'
```

Expected result:

- Response is `200 OK` with the sync run payload, including the run `id`; manual
  sync work continues in the background after the request returns.
- Sync run history shows discovered, skipped, indexed, invalid, failed, and
  suspicious counters.
- Failed package versions do not fail the whole run unless the source cannot be
  processed at all.

## Review Sync

```bash
curl http://localhost:5000/api/admin/sync-runs \
  -H "X-Api-Key: local-dev-key"
```

Then inspect one run:

```bash
curl http://localhost:5000/api/admin/sync-runs/{syncRunId} \
  -H "X-Api-Key: local-dev-key"
```

Expected result:

- Run details include trigger, status, UTC timestamps, summary counters, and
  item-level diagnostics.

## Approve A Package Version

```bash
curl -X POST http://localhost:5000/api/admin/packages/{packageId}/versions/{version}/approve \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  -d '{"reason": "Reviewed for catalog listing"}'
```

Expected result:

- Approval state changes without changing validation status.
- Invalid versions remain hidden from public APIs even if approved.

## Public Discovery

```bash
curl http://localhost:5000/api/packages
curl http://localhost:5000/api/features
```

Expected result:

- Only package versions that are valid, approved, listed, and not suspicious are
  returned.
- Package and feature responses include source/feed provenance and builder-grade
  feature metadata for Runtime Builder clients.

## Runtime Builder Catalog

```bash
curl http://localhost:5000/api/builder/catalog
curl http://localhost:5000/api/builder/infrastructure/providers
```

Expected result:

- The builder catalog returns approved package versions, their source feed,
  manifest-derived features, settings, dependencies, conflicts, required
  capabilities, and abstract infrastructure requirements.
- Infrastructure providers are concrete fulfillment options such as compose
  sidecars or external services. They are separate from package manifests.

## Compatibility Check

```bash
curl -X POST http://localhost:5000/api/compatibility/check \
  -H "Content-Type: application/json" \
  -d '{
    "elsaVersion": "3.0.0",
    "dockerImageVersion": "1.0.0",
    "packages": [
      {
        "packageId": "Elsa.Activities.Email",
        "version": "1.2.3",
        "selectedFeatures": ["email"]
      }
    ]
  }'
```

Expected result:

- Response status is `Pass`, `Warning`, or `Error`.
- Findings identify missing, unapproved, invalid, incompatible, or unknown
  compatibility entries.

## Suspicious Manifest Verification

Use a controlled package fixture where the same package ID and version is served
with different `elsa-package.json` content.

Expected result:

- Existing package-version manifest JSON is not overwritten.
- Package version is marked suspicious.
- Admin sync diagnostics show the original hash and newly observed hash.
- Public APIs hide the suspicious package version.
