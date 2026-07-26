# Quickstart: Server-Side Planning

## Scenario 1: Plan Adds Dependencies

Submit intent with one feature requiring another package.

Expected: response includes auto-added package or blocking finding.

## Scenario 2: Infrastructure Autofill

Submit intent selecting PostgreSQL persistence without infrastructure.

Expected: response selects default PostgreSQL provider when unambiguous.

## Scenario 3: Bundle Uses Plan

Generate bundle for same intent.

Expected: bundle findings and infrastructure match planner output.

## Validation Commands

```bash
dotnet build ValenceControl.sln --no-restore
dotnet test ValenceControl.sln --no-build
```

## Frontend Migration Notes

The frontend should treat `/api/builder/plan` and `/api/workspaces/{workspaceId}/builder/plan` as authoritative for dependency closure, feature auto-adds, infrastructure auto-fill, and planner findings. Local logic may remain temporarily for optimistic display, but saved configuration, resolve, and bundle flows should prefer the server-resolved state.
