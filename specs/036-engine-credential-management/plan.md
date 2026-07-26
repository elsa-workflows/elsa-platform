# Implementation Plan: Engine Credential Management UI

**Branch**: `codex/036-engine-credential-management` | **Date**: 2026-06-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/036-engine-credential-management/spec.md`

## Summary

Add a dedicated workspace-level Console surface for engine credential stores and credential references, outside the new application setup wizard. The implementation reuses the existing workspace deployment credential APIs and TanStack Query data, extracts the current setup-wizard credential panel into a shared management component, adds a route and deployment navigation item, and improves lifecycle flows so administrators can inspect usage, rotate local encrypted credentials, archive items with confirmation, and understand that these credentials are for platform-to-engine communication only.

## Technical Context

**Language/Version**: TypeScript/React for the hosted console; existing C# on .NET 10 APIs remain the backing contract.

**Primary Dependencies**: React Router, TanStack Query, existing deployment console components, existing deployment credential API client, ASP.NET Core minimal API contracts where endpoint behavior must be verified, Vitest and Testing Library for console tests, xUnit and FluentAssertions only if backend contract behavior needs adjustment.

**Storage**: Existing catalog deployment secret-store and credential-reference tables. No schema changes are expected; the feature manages existing workspace-scoped metadata and local protected credential ciphertext through existing APIs.

**Testing**: Focused Vitest coverage for route/navigation, standalone credential management list/create/edit/rotate/archive/usage flows, permission states, and links from engine setup. Existing API tests are sufficient unless endpoint behavior changes; run `npm run test -- src/features/deployments/DeploymentsPage.test.tsx`, `npm run typecheck`, relevant .NET tests if API changes occur, and `git diff --check`.

**Target platform**: Valence Control hosted admin console backed by existing workspace deployment API.

**Project Type**: Modular monolith web/control-plane service with hosted React console.

**Performance Goals**: The standalone management surface remains responsive with at least 50 credential references and does not make provider calls while listing metadata. Usage details are loaded only when a user asks to inspect a reference's usage or initiates a lifecycle action.

**Constraints**: The UI must not display raw secret values, decrypted credentials, provider tokens, or runtime secret data. Engine credential management remains workspace-scoped and governed by existing deployment setup permissions. Runtime secrets and artifact secret references remain out of scope. Archived stores/references remain understandable but unavailable for new assignment.

**Scale/Scope**: Adds one Console management page, navigation/routing, shared credential-management UI, focused tests, and documentation/contracts. Backend changes are limited to missing contract behavior discovered during implementation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Control Plane First**: PASS. The feature manages control-plane metadata for platform-to-engine credentials only and does not reconcile runtime workflow or runtime secret state.
- **Bounded Subsystems**: PASS. Console consumes existing API contracts; deployment core/API/persistence remain bounded unless a missing backend behavior is discovered and added through existing abstractions.
- **Contract Stability**: PASS. The plan reuses additive credential APIs and route additions. Existing setup wizard behavior is preserved while adding a new surface.
- **Safety By Design**: PASS. Raw secret material remains write-only for local encrypted references and external stores remain locator-only. The UI explicitly separates engine credentials from runtime secrets.
- **Incremental Verifiability**: PASS. Navigation, listing, creation, edit, rotation, archive confirmation, usage disclosure, and permission states are independently testable.

Post-design re-check: PASS. The design artifacts keep the feature console-focused, workspace-scoped, safe-metadata-only, and independently testable.

## Project Structure

### Documentation (this feature)

```text
specs/036-engine-credential-management/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── console-engine-credential-management-ux.md
│   └── engine-credential-management-api.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
└── ValenceControl.Console/
    └── src/
        ├── app/
        │   ├── AppShell.tsx
        │   └── routes.tsx
        ├── features/deployments/
        │   ├── DeploymentsPage.tsx
        │   ├── DeploymentsPage.test.tsx
        │   ├── deploymentApi.ts
        │   └── deploymentModels.ts
        └── lib/query/
            └── queryClient.tsx

tests/
├── ValenceControl.Api.Tests/
└── ValenceControl.Console/
```

**Structure Decision**: Keep the management UI inside the existing deployments console feature because the records are deployment setup metadata and share permissions, query keys, API clients, and engine assignment workflows. Avoid adding a separate secrets subsystem because runtime secrets remain outside Valence Control.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
