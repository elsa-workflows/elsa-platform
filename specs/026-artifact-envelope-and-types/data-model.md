# Data Model: Artifact Envelope And Types

## ArtifactEnvelope

Workspace-owned immutable metadata wrapper for a deployable artifact.

Fields:

- `workspaceId`: Tenant boundary.
- `artifactId`: Stable logical artifact identity within a workspace.
- `envelopeVersion`: Version of the envelope contract.
- `artifactTypeId`: Registered artifact type, such as `elsa.workflow-definition`.
- `artifactSchemaVersion`: Producer/runtime schema version for the artifact payload type.
- `contentDigest`: Digest for the artifact content addressed by the payload reference.
- `manifestDigest`: Optional digest for a normalized manifest or descriptor when available.
- `payloadReference`: Provider-specific pointer to payload content outside catalog tables.
- `producer`: Producer metadata.
- `displayMetadata`: Safe labels, annotations, title, description, and source references for UI/search.
- `compatibilityHints`: Runtime and environment compatibility hints.
- `diagnostics`: Safe structured diagnostics.
- `submittedByAccountId`: Actor that submitted or registered the envelope.
- `submittedAt`, `createdAt`, `updatedAt`: Audit timestamps.

Rules:

- Immutable fields cannot change for an existing artifact identity.
- Raw payload content, workflow definition JSON, manifest JSON, tokens, passwords, connection strings, and raw secrets are forbidden.
- Duplicate submissions with identical immutable metadata are idempotent.
- Conflicting duplicate submissions fail closed.

## ArtifactTypeDefinition

Stable definition of an artifact type that producers and runtime appliers can agree on.

Fields:

- `typeId`: Stable identifier, for example `elsa.workflow-definition`.
- `displayName`: Safe human-readable name.
- `description`: Safe description of the type purpose.
- `ownedBy`: Elsa Control or extension owner.
- `supportedSchemaVersions`: Accepted schema version range or set.
- `enabled`: Whether new submissions are accepted.
- `defaultCompatibilityHints`: Optional baseline hints for this type.

Rules:

- Unknown or disabled type IDs are rejected unless explicitly registered by an extension.
- Type IDs are stable and cannot be repurposed for incompatible payload semantics.

## ArtifactProducer

Describes the system that produced or submitted the artifact.

Fields:

- `producerType`: `studio`, `cli`, `ci`, `manual`, or extension-defined value.
- `producerName`: Safe display name.
- `producerVersion`: Optional producer package/application version.
- `sourceReference`: Safe source pointer such as Studio workflow ID, repository ref, build ID, or manual registration source.
- `submittedBy`: User or service actor metadata already authorized by the platform.

Rules:

- Producer metadata is for traceability only and does not grant permission.
- Credentials, authorization headers, webhook secrets, and token values are forbidden.

## PayloadReference

Pointer to artifact payload content outside catalog tables.

Fields:

- `provider`: `local`, `object-storage`, `oci`, `producer-managed`, or extension-defined value.
- `uri`: Provider-scoped reference.
- `mediaType`: Optional content media type.
- `sizeBytes`: Optional size.
- `referenceDigest`: Optional digest for the referenced object.
- `expiresAt`: Optional reference expiration.

Rules:

- Unsupported providers fail closed during inspection or deployment planning.
- References are metadata only; the catalog does not copy payload content in this slice.

## SafeDisplayMetadata

User-facing metadata available for search, listing, and comparison.

Fields:

- `name`: Optional display name.
- `version`: Optional display version.
- `description`: Optional safe description.
- `labels`: Small key/value set for filtering.
- `annotations`: Safe key/value metadata for UI and automation.
- `source`: Optional safe source summary.

Rules:

- Secret-like keys and values are rejected or redacted.
- Values are bounded in length and count.
- Raw workflow JSON, manifest JSON, and provider credentials are forbidden.

## CompatibilityHint

Structured metadata for deployment validation and runtime targeting.

Fields:

- `requiredArtifactType`: Artifact type required by the target runtime.
- `runtimeFamily`: Optional runtime family such as `elsa-workflows`.
- `runtimeVersionRange`: Optional semantic version range.
- `requiredCapabilities`: Capability IDs required from the target runtime.
- `environmentConstraints`: Optional safe constraints for target environment selection.

Rules:

- Hints are used for preflight compatibility; runtime appliers remain authoritative for final validation.
- Hints cannot override workspace permissions, deployment tier safeguards, or runtime health gates.

## EnvelopeDiagnostic

Safe validation or inspection message.

Fields:

- `code`: Stable diagnostic code.
- `severity`: `info`, `warning`, or `error`.
- `message`: Safe human-readable message.
- `target`: Optional safe field or metadata target.

Rules:

- Diagnostics never include raw payload fragments, absolute local paths, credentials, or secret values.
