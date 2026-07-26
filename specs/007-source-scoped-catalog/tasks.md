# Tasks: Source-Scoped Catalog And Account Roadmap

**Input**: Design documents from `/specs/007-source-scoped-catalog/`

## Phase 1: Public Source Filtering

- [X] T001 Add public browseable-source query contract in `src/ValenceControl.PackageCatalog.Core/Sources/`
- [X] T002 Add EF Core public source query adapter returning enabled, non-deleted, browseable public sources
- [X] T003 Add public `/api/sources` endpoint and response contract
- [X] T004 Add URL sanitization tests for public source responses
- [X] T005 Extend public package query methods to accept selected source IDs
- [X] T006 Add source-filter-aware cache keys to `PublicCatalogQueryService`
- [X] T007 Add API tests proving `/api/packages?sourceIds=...` excludes unselected sources
- [X] T008 Add duplicate-package test data across two sources

## Phase 2: Source-Qualified Package Identity

- [X] T009 Replace public package detail endpoints with source-qualified routes
- [X] T010 Update version list and version detail endpoints to require source ID
- [X] T011 Update public query methods to resolve by `sourceId + packageId`
- [X] T012 Add not-found tests for packages missing from the requested source
- [X] T013 Remove or disable global package detail route tests
- [X] T014 Update OpenAPI/API contract documentation for source-qualified package identity

## Phase 3: Builder Source Filtering

- [X] T015 Add `sourceIds` filtering to `/api/builder/catalog`
- [X] T016 Add `sourceId` to builder selected package requests
- [X] T017 Update compatibility query/request models to resolve source-qualified package versions
- [X] T018 Add builder catalog tests for selected-source filtering
- [X] T019 Add builder resolve tests rejecting package selections without source ID
- [X] T020 Add duplicate-package builder resolve test using different source versions

## Phase 4: Lovable/Public UX Alignment

- [ ] T021 Document Lovable public UX contract for source selector data
- [ ] T022 Ensure source selector uses catalog source IDs rather than arbitrary URLs
- [ ] T023 Ensure package and builder requests include selected source IDs
- [ ] T024 Remove or gate custom feed URL entry for anonymous/free users

## Phase 5: Account And Workspace Foundation

- [ ] T025 Add account, external identity, workspace, workspace member, and entitlement snapshot design migrations
- [ ] T026 Add account/workspace domain services with tests for identity mapping
- [ ] T027 Add workspace source ownership fields and visibility rules
- [ ] T028 Add authorization tests proving workspace sources are hidden from unrelated users

## Phase 6: Auth Integration

- [ ] T029 Add verified external identity abstraction for OIDC/JWT or trusted backend context
- [ ] T030 Reject browser-supplied user IDs in authenticated catalog APIs
- [ ] T031 Add Lovable server-to-server trust contract if direct token validation is unavailable
- [ ] T032 Add tests for `issuer + subject` identity mapping

## Phase 7: Workspace-Owned Custom Feeds

- [ ] T033 Add workspace source CRUD API contracts
- [ ] T034 Add entitlement checks for workspace source creation and sync
- [ ] T035 Add workspace source sync diagnostics
- [ ] T036 Add tests for custom source indexing visibility within the owning workspace

## Phase 8: Entitlements And Customer Service Integration

- [ ] T037 Add entitlement snapshot synchronization boundary
- [ ] T038 Add manual/operator entitlement grant path for early paid access
- [ ] T039 Add central customer-service reconciliation contract
- [ ] T040 Add downgrade behavior tests for over-limit workspaces

## Phase 9: Documentation And Verification

- [ ] T041 Update `specs/001-package-catalog/contracts/openapi.yaml` or successor API docs
- [ ] T042 Update quickstart examples after implementation
- [ ] T043 Run API/Core/Persistence test suites
- [ ] T044 Run frontend/UX integration tests where the Lovable-facing client contract is implemented
