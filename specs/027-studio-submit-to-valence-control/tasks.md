# Tasks: Studio Submit To Valence Control

**Input**: Design documents from `specs/027-studio-submit-to-valence-control/`

**Tests**: Required because this feature creates a producer integration, security boundary, and user-facing terminology change.

## Phase 1: Contracts

- [x] T001 Define Studio submit package boundaries and configuration contract.
- [x] T002 Define workflow submission request/response contract aligned with the artifact envelope.
- [x] T003 Define safe Studio UX states for submitted, duplicate, unauthorized, validation failed, unavailable, and retryable errors.

## Phase 2: Valence Control Submission API Compatibility

- [x] T004 Verify existing workspace artifact API can accept Studio-produced `elsa.workflow-definition` envelopes.
- [x] T005 Add API tests for Studio producer metadata, source references, duplicate submit, unsafe metadata, and no deployment side effects.
- [x] T006 Add any missing Valence Control contract fields required by Studio submission without storing raw workflow content in catalog tables.

## Phase 3: Studio Integration Package

- [x] T007 Create the Studio integration package skeleton.
- [x] T008 Add configuration options for Valence Control endpoint, workspace, authentication, and publish separation policy.
- [x] T009 Add **Submit to Valence Control** command and result state model.
- [x] T010 Package workflow snapshot, safe metadata, source identifiers, schema version, digest, and payload reference.
- [x] T011 Submit artifact envelope to Valence Control and handle idempotent duplicate results.

## Phase 4: Verification

- [x] T012 Add unit tests for snapshot packaging and unsafe metadata handling.
- [x] T013 Add integration tests for successful submit and duplicate submit.
- [x] T014 Add tests proving submit does not create a deployment run or runtime publish side effect.
- [x] T015 Run focused checks and record results in `quickstart.md`.
