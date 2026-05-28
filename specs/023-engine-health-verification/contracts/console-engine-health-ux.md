# Contract: Console Engine Health UX

The Deployments page remains the primary workspace cockpit for this slice.

## Engine Registration View

Shows:

- Current engine health badge.
- Last heartbeat time.
- Last manual verification time.
- Version.
- Certificate status.
- Credential verification status.
- Safe verification diagnostic message.
- Verify action for users with `deployments.setup.manage`.

Does not show:

- Raw engine API credentials.
- Provider tokens.
- Raw provider or engine error payloads.
- Stack traces.

## Manual Verification Flow

1. User opens Deployments > Engine Registration.
2. User selects an engine.
3. If the user has setup permission, the Verify button is enabled.
4. User clicks Verify.
5. UI shows pending state for that engine.
6. API returns verification result.
7. UI shows success, degraded, or unreachable state and refreshes cockpit data.

## Runtime Control Availability

- Controls remain hidden or disabled unless matching capability and permission gates pass.
- Controls remain disabled when selected engine health is `Unreachable`.
- Controls become eligible when engine health is `Healthy` or `Degraded` only if the server-side runtime-control gate allows the selected control.
- The UI must explain that verification or fresh heartbeat is required when controls are blocked because health is unreachable.

## Required States

- Never verified: show Verify action and explain that reachability has not been established.
- Verifying: show pending state and prevent duplicate verification clicks for the same engine.
- Healthy: show successful metadata and allow supported controls subject to permission/capability/confirmation gates.
- Degraded: show safe diagnostic; controls remain subject to server gating.
- Unreachable: show safe diagnostic; controls disabled.
- Permission blocked: Verify action disabled with setup permission note.
- Unexpected error: show safe error message without sensitive response details.

## Refresh Rules

- Manual verification invalidates deployment cockpit data.
- Accepted heartbeat updates are visible after the next cockpit refresh.
- Runtime control execution still invalidates cockpit data after success.
