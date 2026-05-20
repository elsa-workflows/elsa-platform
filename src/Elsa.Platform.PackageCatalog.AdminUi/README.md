# Elsa Catalog Admin UI

Lightweight operational dashboard for Elsa Package Catalog administrators.

## Development

```bash
npm install
npm run dev
```

Set `VITE_CATALOG_API_PROXY_TARGET` in a local `.env` file. The browser client
uses relative `/api` requests by default so the Vite dev proxy can avoid CORS
requirements. Admin access is provided by the API host's dashboard session
cookie, not by a browser-readable API key.

## Verification

```bash
npm test
npm run typecheck
npm run build
```

Package details coverage lives in `src/features/packages/PackageDetailsPage.test.tsx`
and the Playwright smoke test in `tests/Elsa.Platform.PackageCatalog.AdminUi.E2E/package-details.spec.ts`.
The page covers canonical package casing, version routes, visibility blockers,
validation findings, feature/settings inspection, manifest review, and
version-scoped approval/rejection actions.

The MVP must expose only Overview, Sources, Packages, and Sync Runs. It must not
include Settings, package identity approval controls, hard-delete source
controls, realtime streaming logs, or manifest editing.

## Deployment

The production API container builds this app and serves it from `/admin`. Vite is
configured with `/admin/` as its asset base path, and browser API calls remain
same-origin `/api` requests.
