# Contract: Console Artifact Envelope UX

## Artifact List

The Artifacts page shows envelope-backed fields for each artifact:

- Display name and version from safe metadata.
- Artifact type, such as `elsa.workflow-definition`.
- Producer type and producer name.
- Digest and checksum/inspection state.
- Compatibility summary, including runtime family and required capabilities.
- Submission time and submitter when available.

The list must continue to support legacy artifacts by displaying default type and producer metadata when explicit envelope fields are absent.

## Artifact Detail

The detail panel shows:

- Immutable artifact identity.
- Envelope version and artifact schema version.
- Payload reference provider and safe reference summary.
- Producer metadata.
- Safe labels and annotations.
- Compatibility hints.
- Diagnostics and inspection status.

The detail panel must not show payload content, workflow definition JSON, manifest JSON, credentials, tokens, connection strings, or raw secret values.

## Type Filter

Users can filter or scan artifacts by artifact type. The first required type is `elsa.workflow-definition`.

## Empty And Error States

- Empty state explains that artifacts appear after producer submission or manual registration.
- Unknown artifact type errors identify the type ID but do not echo raw payload content.
- Unsafe metadata errors identify rejected keys without echoing secret values.
