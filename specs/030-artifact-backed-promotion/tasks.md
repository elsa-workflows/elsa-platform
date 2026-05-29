# Tasks: Artifact Backed Promotion

**Input**: Design documents from `specs/030-artifact-backed-promotion/`

**Tests**: Required because this feature changes promotion, deployment, rollback, validation, and persistence semantics.

## Phase 1: Data Model And Contracts

- [x] T001 Define artifact-backed desired-state revision model.
- [x] T002 Define artifact reference validation and safe command reference contract.
- [x] T003 Define promotion preview output for artifact identity, digest, type, safe metadata, configuration, tier policy, and runtime compatibility.

## Phase 2: Persistence

- [x] T004 Add persistence for artifact references in desired-state revisions or structured desired-state records.
- [x] T005 Add SQLite and SQL Server migrations.
- [x] T006 Add backfill/read compatibility for existing structured desired-state records.
- [x] T007 Add persistence tests for workspace isolation, missing artifact, duplicate references, and no raw payload storage.

## Phase 3: Promotion And Deployment

- [x] T008 Update promotion preview to compare artifact-backed revisions.
- [ ] T009 Update promotion creation to create artifact-backed target revisions.
- [ ] T010 Update deployment validation to check artifact ownership, type, digest, payload reference availability, tier policy, and runtime capability hints.
- [x] T011 Update deployment command creation to include artifact references.
- [ ] T012 Add rollback selection and validation for artifact-backed revisions.

## Phase 4: API And Console

- [ ] T013 Add API request/response contract changes for artifact-backed revisions.
- [ ] T014 Update console promotion and run detail views to show safe artifact metadata.
- [ ] T015 Add API and console tests for preview, promote, deploy, and rollback.

## Phase 5: Verification

- [ ] T016 Run focused core, persistence, API, console, and `git diff --check` verification.
- [ ] T017 Record results in `quickstart.md`.
