# Tasks: Organization Tenancy

**Input**: Design documents from `specs/031-organization-tenancy/`

## Phase 1: Spec Alignment

- [X] T001 Create organization tenancy feature specification in `specs/031-organization-tenancy/spec.md`
- [X] T002 Create cross-spec amendment plan in `specs/031-organization-tenancy/amendment-plan.md`
- [X] T003 Create implementation plan, research, data model, API contract, quickstart, and checklist artifacts
- [X] T004 Add forward-compatibility notes to existing tenant-boundary specs
- [X] T005 Update `AGENTS.md` current Spec Kit plan reference to organization tenancy

## Phase 2: Domain Foundation

- [X] T006 Add organization and organization membership models in PackageCatalog core
- [X] T007 Add organization-aware account/workspace context models
- [X] T008 Add organization role definitions separately from workspace roles
- [X] T009 Add service tests for first sign-in organization provisioning
- [X] T010 Add service tests proving organization membership alone does not authorize workspace-owned resource access

## Phase 3: Persistence And Migration

- [X] T011 Add EF entities/mappings for organizations, organization memberships, organization entitlements, and organization audit records
- [X] T012 Add `OrganizationId` to workspace persistence
- [X] T013 Add SQLite migration to backfill organizations for existing workspaces
- [X] T014 Add SQL Server migration to backfill organizations for existing workspaces
- [X] T015 Add persistence tests proving workspace IDs and workspace-owned resource IDs remain stable

## Phase 4: API And Authorization

- [X] T016 Add `GET /api/me/organizations`
- [X] T017 Add organization workspace list/create/update endpoints
- [X] T018 Add workspace membership management endpoint under organization routes
- [X] T019 Update workspace authorization resolution to include owning organization
- [X] T020 Preserve compatibility for existing `/api/workspaces/{workspaceId}` routes
- [X] T021 Add API tests for cross-organization and same-organization cross-workspace denial

## Phase 5: Console Integration

- [X] T022 Add organization/workspace context client models
- [X] T023 Add organization/workspace switcher UI
- [X] T024 Update workspace feature route guards to require selected workspace inside selected organization
- [X] T025 Replace user-facing "Workspace tenant boundary" copy with organization/workspace hierarchy language
- [X] T026 Add console tests for multiple organizations and workspace access states

## Phase 6: Cleanup

- [X] T027 Deprecate or remove `WorkspaceKind.Organization` where safe
- [X] T028 Update spec references that still describe Workspace as the customer tenant boundary
- [X] T029 Run focused backend and console tests from `quickstart.md`
- [X] T030 Run `git diff --check`
