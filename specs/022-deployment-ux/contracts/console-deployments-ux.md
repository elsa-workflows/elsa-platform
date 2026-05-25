# Contract: Console Deployments UX

The console Deployments route is a customer workspace feature. It must use live workspace APIs and must not render seeded sample deployment data.

## Route

- `/admin/deployments`
- Requires customer workspace authentication.
- Reads effective deployment permissions before enabling setup, desired-state, preview, deploy, rollback, observability, or control actions.
- If the user has multiple workspaces, the first implementation may use the current default workspace selection pattern, but API calls must remain workspace-scoped.

## Primary Views

### Cockpit

Shows:

- Workflow application selector.
- Environment table with health, desired revision, deployed revision, drift, deployment status, and engine count.
- Recent deployment history.
- Empty state when no workflow applications exist.

Actions:

- Create workflow application when `deployments.setup.manage` is granted.
- Create environment when `deployments.setup.manage` is granted.
- Inspect environment.

### Engine Registration

Shows:

- Environment selector.
- Registered engines for the selected environment.
- Endpoint, credential reference metadata, health, certificate status, capabilities, and available controls.

Actions:

- Register engine.
- Update engine metadata.
- Refresh cockpit data after mutations.

Credential handling:

- The UI may display provider and reference strings.
- The UI must never display raw secret values or provider tokens.

### Promotion Preview

Shows:

- Source environment/revision selector.
- Target environment/engine selector.
- Categorized diff.
- Validation panel with pass, warning, and blocker states.
- Rollback candidate when available.

Actions:

- Preview promotion.
- Start dry-run or deployment only when no blockers exist, `deployments.run.execute` is granted, and the user explicitly confirms the action.

### Deployment Runs

Shows:

- Active run status.
- Recent history.
- Actor, target environment, target engine, source revision, deployed revision, validation outcome, status, and timestamps.

Actions:

- Open run details.
- Roll back from a compatible successful run only when `deployments.rollback.execute` is granted and the user explicitly confirms the action.

### Runtime Controls

Shows:

- Controls filtered by engine capabilities.
- Boundary labels for workflow, engine API, shell, and hosting operations.

Actions:

- Execute supported controls when current permissions and entitlement allow it, after explicit confirmation.
- Show unavailable states for missing hosting provider or missing capability.

### Observability And Drift

Shows:

- Persisted observability binding metadata.
- Persisted drift status and drift report metadata.

Does not:

- Pull live logs, traces, metrics, or console streams from external providers in this slice.
- Perform live drift detection against registered engines in this slice.

## Required States

- Loading: shown while cockpit or mutation data is pending.
- Empty: shown when no workflow applications or no engines exist.
- Unauthorized: shown when customer identity or workspace access is missing.
- Validation blocked: deploy and rollback controls disabled with blocker details visible.
- Permission blocked: action controls disabled when the caller lacks the required permission grant.
- Confirmation required: deploy, rollback, and runtime controls show a confirmation step before the API mutation is submitted.
- Running: active deployment run status visible after start and on refresh.
- Succeeded/failed: final state visible in history.
- Unexpected: safe error message without sensitive response details.

## Refresh Rules

- Creating application, environment, engine, structured revision, queued run, rollback, or runtime control invalidates cockpit data.
- Starting deployment or rollback invalidates run detail and history data.
- Preview does not mutate cockpit state.

## Accessibility And Interaction

- Forms must expose labels for all inputs.
- Destructive or runtime-mutating actions require explicit confirmation by the initiating user.
- Disabled actions must have visible state tied to validation, authorization, or capability data.
