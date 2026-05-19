# Quickstart: Source-Scoped Catalog And Account Roadmap

## Phase 1: Public Source Filtering

1. Seed or configure at least two public package sources.
2. Ensure each source has approved, listed, valid package versions.
3. Request browseable sources:

```bash
curl http://localhost:5000/api/sources
```

4. Request packages from one source:

```bash
curl "http://localhost:5000/api/packages?sourceIds=00000000-0000-0000-0000-000000000001"
```

Expected result: only packages from the selected source are returned.

## Phase 2: Source-Qualified Details

Request package details through the source-qualified route:

```bash
curl "http://localhost:5000/api/sources/00000000-0000-0000-0000-000000000001/packages/Elsa.Email"
```

Request versions:

```bash
curl "http://localhost:5000/api/sources/00000000-0000-0000-0000-000000000001/packages/Elsa.Email/versions"
```

Request a specific version:

```bash
curl "http://localhost:5000/api/sources/00000000-0000-0000-0000-000000000001/packages/Elsa.Email/versions/1.0.0"
```

Global package detail routes are not part of this feature's target contract.

## Phase 3: Builder Flow

Request a filtered builder catalog:

```bash
curl "http://localhost:5000/api/builder/catalog?sourceIds=00000000-0000-0000-0000-000000000001"
```

Resolve source-qualified selections:

```bash
curl -X POST http://localhost:5000/api/builder/resolve \
  -H "Content-Type: application/json" \
  -d '{
    "packages": [
      {
        "sourceId": "00000000-0000-0000-0000-000000000001",
        "packageId": "Elsa.Email",
        "version": "1.0.0"
      }
    ]
  }'
```

## Later Account/Workspace Validation

When account-owned feeds are implemented:

1. Sign in through a verified identity provider or trusted backend client.
2. Confirm the catalog maps the external subject to an internal account.
3. Confirm a personal workspace exists.
4. Confirm entitlement snapshot allows custom feeds.
5. Create a workspace-owned source.
6. Sync the source.
7. Browse packages from public and workspace-owned selected sources.
