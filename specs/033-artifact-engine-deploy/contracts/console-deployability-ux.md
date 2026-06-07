# Contract: Console Deployability UX

The console deployability UX lives primarily on the desired-state revision detail page and uses the deployability API as its source of truth.

## Revision Detail Deploy Panel

Required behavior:

- Show a target engine selector scoped to the revision environment.
- Evaluate deployability when a target engine is selected and whenever relevant cockpit/revision data refreshes.
- Disable `Deploy revision` while deployability is loading, blocked, or the user lacks deployment execution permission.
- Show a compact status summary: Deployable, Warnings, or Blocked.
- Show blockers as actionable rows with scope, message, and remediation.
- Keep run history as the authoritative state after queueing.

Status examples:

```text
Deployable
All 1 artifact records can be applied by dev-01.
```

```text
Blocked
dev-01 is missing artifact.elsa.workflow-definition.apply.
Action: Refresh the engine heartbeat or install the workflow definition runtime applier.
```

## Artifact Rows

Each artifact referenced by the revision should show:

- Safe display name.
- Artifact type and schema version.
- Expected digest in a wrapping/monospace container.
- Required capabilities.
- Payload availability.
- Per-artifact status.

Display rules:

- Long digests and references wrap inside their containers.
- Console download links use the operator artifact download endpoint and show a safe file name or action label, not a raw local path.
- Raw storage references may be available to authorized setup users only if deliberately exposed in an inspection/debug view; they are not the primary deployment interface.

## Blocker Copy

Blockers must map to explicit user actions:

- Missing canonical capability: "Install or enable the runtime applier that advertises `artifact.{artifactTypeId}.apply`, then refresh engine heartbeat."
- Stale engine metadata: "Reconnect the engine or wait for a fresh heartbeat before deploying."
- Artifact unavailable: "Refresh inspection, restore the artifact, or fix the artifact storage reference."
- Archived artifact: "Restore the artifact or create a new revision using an active artifact."
- Unsupported schema: "Use a compatible artifact version or upgrade the runtime applier."
- Permission missing: "Ask a workspace owner for deployment execution permission."

## New Revision Form Interaction

This feature does not redesign the environment/engine creation workflow, but the revision form must only show tier-driven required records that apply to the selected environment. For example, observability binding UI appears when the target tier requires observability, not unconditionally for Dev.

## Accessibility And State

- Deployability status changes use `role="status"` where appropriate.
- Blocking errors use `role="alert"` only for actionable errors that need immediate attention.
- Buttons keep stable dimensions while loading.
- The deploy action has a deterministic disabled reason visible near the button.
