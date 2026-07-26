# Contract: Console Artifacts UX

The Artifacts route becomes a real workspace feature instead of a disabled placeholder.

## Navigation

- The Valence Control navigation shows Artifacts as enabled.
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

Manual registration remains an advanced path for CI, local/test references, and integration debugging. It should not be the primary console path after artifact upload is implemented.

## Upload Flow (Follow-up)

1. User opens Artifacts.
2. User with setup permission chooses Upload artifact.
3. UI opens a dedicated upload page or wizard, not an inline form inside the list/detail view.
4. User drops or selects a ZIP artifact file.
5. UI displays file name, size, expected type, optional client-side SHA-256 progress when feasible, and a warning that the server computes the authoritative digest.
6. UI creates an upload session through the workspace API.
7. UI uploads bytes through the returned API stream or short-lived provider-direct upload URL.
8. UI completes the upload session and shows processing status while the backend computes digest, inspects the artifact envelope, extracts manifest/resource summaries, and creates the artifact record.
9. On success, UI navigates to the artifact detail page for the created or existing duplicate artifact record.
10. On failure, UI shows safe diagnostics and lets the user retry, cancel, or choose a different file.

Upload UX rules:

- The default action label should be Upload artifact; Register artifact should be secondary/advanced.
- Users should not type artifact identity, digest, manifest name, resource count, or reference for uploaded artifacts.
- Progress states should distinguish uploading bytes from server-side inspection/processing.
- The UI must not render raw manifest JSON, workflow definition content, file contents, storage URLs with credentials, or secret values.
- Upload errors should be actionable: invalid artifact layout, unsupported ZIP, digest mismatch, file too large, quota exceeded, scan failed, duplicate artifact, expired session, or network interruption.
- Abandoned upload sessions should be resumable only when the backend supports the same idempotency key and staged object; otherwise the UI should restart cleanly.

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
- Uploading: byte-transfer progress and cancel action.
- Processing upload: server-side digest/inspection progress after bytes are staged.
- Upload failed: safe diagnostics with retry/cancel.
- Invalid: checksum or identity mismatch with safe diagnostics.
- Permission blocked: registration and refresh disabled when setup permission is absent.
- Unauthorized/unexpected: shared request state views.
