# Elsa Platform Console

Unified React console for Elsa Platform workspace users and operators. Package
Catalog is the first active module; deployment artifacts, Runtime Builder,
environment workbenches, managed runtimes, runtime operations, and audit views
are reserved as platform modules from the beginning.

## Development

```bash
npm install
npm run dev
```

The Vite dev server proxies relative `/api` requests to the package catalog API
host at `http://localhost:5220` by default so the browser client can avoid CORS
requirements. Override `CATALOG_API_PROXY_TARGET` in a local `.env` file
when the API runs elsewhere. In development, the proxy also forwards local
trusted workspace identity headers for workspace-scoped Runtime Builder APIs.
Admin access is provided by the API host's dashboard session cookie, not by a
browser-readable API key.

When running `src/Elsa.Platform.AppHost`, Aspire starts this Vite app as the
`console` resource and injects the API endpoint as `CATALOG_API_PROXY_TARGET`.

## Verification

```bash
npm test
npm run typecheck
npm run build
```

Package details coverage lives in `src/features/packages/PackageDetailsPage.test.tsx`
and the Playwright smoke test in `tests/Elsa.Platform.Console.E2E/package-details.spec.ts`.
The page covers canonical package casing, version routes, visibility blockers,
validation findings, feature/settings inspection, manifest review, and
version-scoped approval/rejection actions.

The first active module exposes Overview, Sources, Packages, and Sync Runs.
Deployment, artifact, Runtime Builder, target, runtime, operations, and audit
modules may be visible as roadmap affordances but must not imply implemented
mutations before backend contracts exist. The catalog module must not include
Settings, package identity approval controls, hard-delete source controls,
realtime streaming logs, or manifest editing.

## Deployment

The production API container builds this app and serves it from `/admin`. Vite is
configured with `/admin/` as its asset base path, and browser API calls remain
same-origin `/api` requests.
