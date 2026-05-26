# Contract: Console Artifacts UX

The Artifacts route becomes a real workspace feature instead of a disabled placeholder.

## Navigation

- The Platform navigation shows Artifacts as enabled.
- Authenticated workspace users can open `/admin/artifacts`.
- Users without a workspace see the shared no-workspace state.

## Artifact List

Shows:

- Artifact identity.
- Source manifest name/version/environment.
- Format.
- Resource count.
- Checksum status.
- Inspection status.
- Registered timestamp.
- Last inspected timestamp.

Does not show:

- Raw artifact payloads.
- Manifest JSON.
- Workflow definition content.
- Provider tokens.
- Secret values.

## Detail View

Shows:

- Immutable identity and digest.
- Layout version.
- Reference provider and reference.
- Safe manifest summary.
- Resource summaries.
- Checksum and inspection state.
- Safe diagnostics.

## Registration Flow

1. User opens Artifacts.
2. User with setup permission chooses Register artifact.
3. User enters artifact identity, digest, format, reference provider/reference, manifest summary, and resource summary metadata.
4. UI submits through the workspace API.
5. List refreshes and the new artifact is selected.

## Refresh Flow

1. User selects an artifact.
2. User with setup permission chooses Refresh inspection.
3. UI shows pending state.
4. API returns valid, invalid, unavailable, or unsupported inspection state.
5. UI refreshes artifact detail and list.

## Required States

- Empty: no registered artifacts and a setup-permission-aware register action.
- Loading: list/detail fetch in progress.
- List/detail: live workspace artifact data.
- Invalid: checksum or identity mismatch with safe diagnostics.
- Permission blocked: registration and refresh disabled when setup permission is absent.
- Unauthorized/unexpected: shared request state views.
