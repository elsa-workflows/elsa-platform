# Contract: Weaver Console UX

## Entry Point

The existing global Weaver button opens a drawer. The drawer must work on desktop and mobile shell layouts.

## Drawer States

- `Unavailable`: Weaver disabled or misconfigured. Shows safe reason and configuration link for admins.
- `Idle`: No active turn. Shows route context, suggested prompts, mode selector, and message input.
- `Working`: Active streamed response. Shows assistant deltas, visible tool timeline, cancel control, and disabled send button.
- `Queued`: Additional prompts queued while a turn is running.
- `WaitingForApproval`: A generated plan needs human review.
- `Error`: Provider/runtime/configuration error with retry or configuration guidance.

## Message Surface

- User messages appear on the right.
- Assistant messages appear on the left and may cite platform objects.
- Tool activity appears as a compact collapsible timeline.
- Redacted values are clearly marked as redacted.

## Plan Surface

Plan cards show:

- Plan title and type
- Status
- Target workspace/application/environment/engine/revision/artifact
- Expected impact
- Validation results and blockers
- Risk level
- Approval boundary
- Rollback/remediation path
- Approve, reject, execute, or view details controls depending on permission and status

## Mode Selection

Modes:

- `Inspect`: Read-only explanations and investigation.
- `Plan`: Read-only investigation plus immutable plan drafting.
- `Operate`: Approved plan execution only; cannot execute directly from free-form text.

## Configuration Documentation Link

When Weaver is unavailable or misconfigured, the drawer links to configuration documentation in `docs/weaver-configuration.md`.
