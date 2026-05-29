# Tasks: Workflow Artifact Runtime Applier

**Input**: Design documents from `specs/029-workflow-artifact-runtime-applier/`

**Tests**: Required because this package mutates runtime workflow state and must prove idempotency, digest validation, and safe diagnostics.

## Phase 1: Runtime Package Contract

- [x] T001 Define runtime integration package boundaries and configuration.
- [x] T002 Define runtime capability advertisement for workflow artifact support.
- [x] T003 Define payload fetch, digest verification, schema validation, and apply result contracts.

## Phase 2: Sync Worker

- [x] T004 Implement runtime command polling and claim behavior.
- [x] T005 Implement heartbeat and progress reporting.
- [x] T006 Implement completion, failure, and rejection reporting.
- [x] T007 Add lease-expiration and retry handling.

## Phase 3: Workflow Artifact Applier

- [ ] T008 Implement workflow artifact payload loading.
- [ ] T009 Implement digest and schema compatibility validation.
- [ ] T010 Implement local runtime workflow definition apply.
- [ ] T011 Implement apply journal/idempotency guard.
- [ ] T012 Implement safe diagnostics and runtime reference reporting.

## Phase 4: Verification

- [ ] T013 Add tests for successful apply.
- [ ] T014 Add duplicate-delivery/idempotency tests.
- [ ] T015 Add digest mismatch, unsupported schema, and local validation failure tests.
- [ ] T016 Add safe diagnostics tests.
- [ ] T017 Run focused checks and record results in `quickstart.md`.
