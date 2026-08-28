# Contract: Progress Tracking

## Source Of Truth

Implementation progress is tracked in [tasks.md](../tasks.md).

Rules:

- Every implementation task uses a markdown checkbox.
- Completed tasks are checked in the same commit or PR that completes the work.
- Blocked tasks get a short blocker note directly below the task.
- Phase gates must not be marked complete until verification commands or review notes are recorded.
- Mechanical migration tasks and architecture improvement tasks remain separate.

## Phase Status Values

- `NotStarted`
- `InProgress`
- `Blocked`
- `Complete`
- `Reopened`

## Required Progress Checkpoints

- After import builds.
- After existing tests pass.
- After project rename builds.
- After dependency boundary checks pass.
- After deployment-facing catalog abstraction exists.
- After old repository README points to `elsa-control`.
