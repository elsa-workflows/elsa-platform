# Data Model: Deployment Artifact Registry

## WorkspaceDeploymentArtifact

Workspace-owned registry entry for one immutable deployment artifact.

Fields:

- `Id`: artifact record identifier.
- `WorkspaceId`: owning workspace.
- `ArtifactId`: immutable content-derived artifact identity.
- `LayoutVersion`: artifact layout version, expected to be `elsa-control/deployment-artifact/v1alpha1`.
- `ContentDigestAlgorithm`: digest algorithm, expected to be SHA-256 for Phase 1.
- `ContentDigest`: content digest value.
- `Format`: folder, zip, or unknown reference format.
- `ReferenceProvider`: local/test, external, or future provider identifier.
- `Reference`: provider/file reference, never raw payload.
- `ManifestName`: source manifest name when available.
- `ManifestVersion`: source manifest version when available.
- `ManifestEnvironment`: source manifest environment when available.
- `ResourceCount`: number of resources summarized from artifact metadata.
- `ResourceSummaryJson`: safe summary of resource type/logical ID/scope/version/hash metadata.
- `ChecksumStatus`: verified, missing, mismatched, unexpected, unavailable, or unverified.
- `InspectionStatus`: never inspected, valid, invalid, unavailable, or unsupported.
- `DiagnosticsJson`: safe diagnostics.
- `RegisteredAt`, `RegisteredByAccountId`: registration audit metadata.
- `LastInspectedAt`: latest inspection refresh timestamp.
- `CreatedAt`, `UpdatedAt`: persistence timestamps.

Validation rules:

- `WorkspaceId` scopes every read and mutation.
- `ArtifactId` is unique per workspace.
- Raw payloads, manifest JSON, workflow definitions, tokens, passwords, and secret values are rejected before persistence.
- Unsupported layout versions are rejected or persisted as invalid according to registration path.
- Diagnostics must be safe summaries.

## WorkspaceArtifactResourceSummary

Safe resource summary extracted from artifact metadata.

Fields:

- `Type`: resource type.
- `LogicalId`: logical resource identity.
- `Scope`: optional resource scope.
- `Version`: optional resource version.
- `DesiredStateHashAlgorithm`: optional hash algorithm.
- `DesiredStateHash`: optional desired-state hash.

Validation rules:

- Summaries must not include resource payload content.
- Resource count and summary JSON must agree.

## WorkspaceArtifactDiagnostic

Safe diagnostic emitted during registration or inspection.

Fields:

- `Code`: stable diagnostic code.
- `Severity`: info, warning, or error.
- `Message`: safe user-facing message.

Validation rules:

- Messages must not include raw payload text, secrets, provider tokens, or stack traces.
- Diagnostics may include artifact-relative paths only when safe.

## WorkspaceArtifactRegistrationRequest

Request to register metadata for an existing artifact.

Fields:

- `ArtifactId`
- `LayoutVersion`
- `ContentDigestAlgorithm`
- `ContentDigest`
- `Format`
- `ReferenceProvider`
- `Reference`
- `ManifestName`
- `ManifestVersion`
- `ManifestEnvironment`
- `Resources`
- `Diagnostics`
- `ActorAccountId`

Validation rules:

- Actor must be a workspace member with deployment setup permission.
- Required identity, digest, layout, format, provider, and reference fields must be present.
- Duplicate artifact identity must not create conflicting records.

## WorkspaceArtifactInspectionResult

Latest inspection state returned after refresh.

Fields:

- `ArtifactId`
- `ChecksumStatus`
- `InspectionStatus`
- `LastInspectedAt`
- `ResourceCount`
- `Resources`
- `Diagnostics`

Validation rules:

- Refresh must preserve the registered artifact identity.
- Missing or mismatched referenced artifacts mark the registry record invalid rather than deleting it.

## WorkspaceArtifactUploadSession

Temporary workspace-scoped upload state used by the follow-up artifact upload slice.

Fields:

- `Id`: upload session identifier.
- `WorkspaceId`: owning workspace.
- `IdempotencyKey`: caller-provided or server-issued key for safe retry.
- `FileName`: original client file name after safe normalization.
- `ContentType`: client content type, treated as advisory only.
- `ExpectedSizeBytes`: client-declared size.
- `ReceivedSizeBytes`: size confirmed by the upload provider.
- `ExpectedDigestAlgorithm`: optional client-declared digest algorithm.
- `ExpectedDigest`: optional client-declared digest value.
- `ComputedDigestAlgorithm`: digest algorithm computed by the server or storage verification worker.
- `ComputedDigest`: digest value computed from stored bytes.
- `Format`: expected artifact format, initially ZIP for console upload.
- `StorageProvider`: local/test, object storage, or future provider identifier.
- `StorageReference`: provider-backed staged object reference, never raw payload or credentials.
- `Status`: pending, uploading, uploaded, inspecting, completed, failed, expired, aborted, or cleanup pending.
- `DiagnosticsJson`: safe upload and inspection diagnostics.
- `ArtifactRecordId`: artifact record created after successful completion, when available.
- `CreatedAt`, `ExpiresAt`, `CompletedAt`, `UpdatedAt`: lifecycle timestamps.
- `CreatedByAccountId`: upload initiator.

Validation rules:

- Actor must be a workspace member with deployment setup permission.
- Upload sessions are scoped to one workspace and cannot complete into another workspace.
- Sessions expire and must be cleaned up if not completed within the configured lifetime.
- Uploaded bytes must be stored in artifact blob storage, not catalog database rows.
- File name, content type, and client-supplied digest are advisory until server-side verification completes.
- Duplicate artifact content in the same workspace must not create conflicting artifact records.
- Diagnostics must not include raw payload snippets, manifest JSON, workflow definitions, storage credentials, or secret values.

## WorkspaceArtifactBlobReference

Provider-backed pointer to uploaded artifact bytes.

Fields:

- `Provider`: storage provider identifier.
- `Container`: provider-specific bucket/container name or logical store.
- `ObjectKey`: provider-specific object key or local/test path.
- `ContentLength`: verified content length.
- `ContentDigestAlgorithm`: verified digest algorithm.
- `ContentDigest`: verified digest value.
- `CreatedAt`: storage write timestamp.
- `RetentionState`: retained, quarantine, cleanup pending, or deleted.

Validation rules:

- References must be opaque to clients and must not contain signed URLs, access keys, SAS tokens, connection strings, or credentials.
- Provider adapters must enforce workspace prefixes or equivalent isolation.
- Failed, expired, or aborted uploads must remove or quarantine blob references according to provider capability.
