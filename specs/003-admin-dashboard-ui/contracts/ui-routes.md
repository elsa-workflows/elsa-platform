# UI Routes Contract

The dashboard MVP has exactly four primary destinations.

## Route Map

| Route | Purpose |
|-------|---------|
| `/admin` | Redirects to `/admin/overview`. |
| `/admin/overview` | Lightweight operational status and recent activity. |
| `/admin/sources` | Active package source list, filters, and source actions. |
| `/admin/sources/new` | Create source form. |
| `/admin/sources/:sourceId` | Source details, health, configuration, recent runs. |
| `/admin/sources/:sourceId/edit` | Edit source form. |
| `/admin/packages` | Package list with filters, search, sorting, selection, and bulk actions. |
| `/admin/packages/:packageId` | Package details with selected/latest version context. |
| `/admin/packages/:packageId/versions/:version` | Package version details, validation, manifest viewer, visibility explanation, actions. |
| `/admin/sync-runs` | Sync run list. |
| `/admin/sync-runs/:runId` | Sync run details and item diagnostics. |

No `/admin/settings` route is included in the MVP.

## Query Parameters

### Packages

- `q`: package ID search.
- `approval`: `pending`, `approved`, `rejected`.
- `validation`: `notValidated`, `valid`, `invalid`, `unsupportedSchema`,
  `suspicious`.
- `sourceId`: source filter.
- `listed`: `true` or `false`.
- `suspicious`: `true` or `false`.
- `sort`: `packageId`, `updatedAt`, `approvalStatus`, `validationStatus`,
  `source`.
- `page`: 1-based page index when server pagination exists.

### Sync Runs

- `status`: sync run status.
- `trigger`: sync trigger.
- `sourceId`: source-related runs.
- `packageId`: package-related runs.

### Sources

- `status`: source status.
- `enabled`: `true` or `false`.

## Navigation Behavior

- The sidebar or top-level navigation shows only Overview, Sources, Packages,
  and Sync Runs.
- Summary cards on Overview link to filtered routes.
- Source and sync run diagnostics can link to each other when IDs are available.
- Package version actions return to the same filtered package list or detail
  route after mutation.

## Loading and Error States

Every route must support:

- Initial loading.
- Empty state.
- Filtered-empty state where applicable.
- Refreshing state over previously loaded data.
- Stale state after refresh failure.
- Unauthorized/access-problem state.
- Not-found state for detail routes.

## Accessibility Contract

- Primary navigation is keyboard reachable.
- Tables expose row actions through buttons or menus with accessible names.
- Dialogs trap focus and restore it after close.
- Status badges include text labels and do not rely on color alone.
- Manifest viewer collapsible sections expose expanded/collapsed state.
