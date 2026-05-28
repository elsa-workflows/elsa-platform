# Data Model: Deployment Artifact Registry

## WorkspaceDeploymentArtifact

Workspace-owned registry entry for one immutable deployment artifact.

Fields:

- `Id`: artifact record identifier.
- `WorkspaceId`: owning workspace.
- `ArtifactId`: immutable content-derived artifact identity.
- `LayoutVersion`: artifact layout version, expected to be `platform.elsa.io/deployment-artifact/v1alpha1`.
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
