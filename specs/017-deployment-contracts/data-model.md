# Data Model: Deployment Foundation Contracts

## DeploymentResourceId

Stable identity for desired deployable state.

- `Type`: resource type such as `workflowDefinition`, `variable`, `feature`, `package`, or a future extension type.
- `LogicalId`: stable logical name within the resource type.
- `Scope`: optional environment, tenant, namespace, or handler-defined scope string.

Validation:

- `Type` is required.
- `LogicalId` is required.
- Values are trimmed and empty values are rejected.

## DeploymentResource

Desired control-plane resource entry.

- `Id`: `DeploymentResourceId`.
- `Version`: optional desired version or resource revision.
- `DesiredStateHash`: optional digest of the normalized desired payload.
- `Dependencies`: zero or more `DeploymentResourceId` entries.
- `Deletion`: `DeploymentDeletionBehavior`.
- `Metadata`: string dictionary for safe extension metadata.

Validation:

- `Id` is required.
- Dependencies cannot contain empty resource identities.
- Deletion defaults to conservative retention.

## DeploymentArtifactIdentity

Immutable artifact identity used by validation, diff, dry-run, apply, and history.

- `Id`: artifact id.
- `Version`: optional artifact version.
- `SchemaVersion`: artifact metadata schema version.
- `ManifestDigest`: digest of the manifest content.
- `ContentDigest`: digest of artifact content.

Validation:

- `Id`, `SchemaVersion`, `ManifestDigest`, and `ContentDigest` are required.
- Digests include algorithm and value.

## DeploymentArtifactMetadata

Metadata captured when an artifact is built or inspected.

- `Identity`: `DeploymentArtifactIdentity`.
- `BuiltAt`: timestamp.
- `Builder`: optional builder name/version.
- `Source`: optional source repository, commit, branch, or pipeline metadata.
- `Properties`: string dictionary for safe extension metadata.

Validation:

- Raw secrets are not allowed in metadata values.
- `BuiltAt` uses UTC.

## DeploymentTargetDescriptor

Named destination context for deployment operations.

- `Id`: stable target id.
- `DisplayName`: optional human-readable name.
- `Environment`: optional environment label.
- `Properties`: string dictionary for non-secret target metadata.

Validation:

- `Id` is required.
- Credentials and raw secrets are excluded.

## DeploymentDiagnostic

Structured message emitted during validation, planning, dry-run, apply, and history.

- `Code`: machine-readable code.
- `Severity`: info, warning, error, or fatal.
- `Message`: human-readable message.
- `ResourceId`: optional associated resource.
- `Details`: optional safe key/value metadata.

Validation:

- `Code` and `Message` are required.
- Diagnostics can be associated with a resource but do not require one.

## DeploymentPlan

Deterministic set of changes for a target and artifact.

- `Id`: plan id.
- `Artifact`: `DeploymentArtifactIdentity`.
- `Target`: `DeploymentTargetDescriptor`.
- `Changes`: ordered list of `DeploymentChange`.
- `Diagnostics`: ordered list of `DeploymentDiagnostic`.
- `CreatedAt`: timestamp.

Validation:

- A plan always references one artifact and one target.
- Change ordering is deterministic.

## DeploymentChange

Resource-specific action proposed by a plan.

- `Resource`: `DeploymentResourceId`.
- `Action`: create, update, activate, deactivate, delete, no-op, unsupported, or conflict.
- `Status`: pending, blocked, skipped, ready, or completed in result contexts.
- `Reason`: optional explanation.
- `Diagnostics`: ordered diagnostics.

Validation:

- `Resource` and `Action` are required.
- Conflict and unsupported changes must include a diagnostic or reason.

## DeploymentResult

Outcome of validation, dry-run, or apply.

- `DeploymentId`: unique deployment attempt id.
- `Mode`: validate, diff, dry-run, or apply.
- `Status`: not started, validated, dry-run complete, applied, no-op, completed with warnings, validation failed, partially applied, failed, or cancelled.
- `Artifact`: `DeploymentArtifactIdentity`.
- `Target`: `DeploymentTargetDescriptor`.
- `Plan`: optional plan snapshot.
- `ResourceResults`: ordered per-resource outcomes.
- `Diagnostics`: ordered diagnostics.
- `StartedAt`: timestamp.
- `CompletedAt`: optional timestamp.

Validation:

- Apply results can be partially applied.
- Dry-run results must not imply mutation.
- Failure results must include at least one error or fatal diagnostic.

## DeploymentHistoryRecord

Audit record for a deployment attempt.

- `DeploymentId`: unique deployment attempt id.
- `Artifact`: `DeploymentArtifactIdentity`.
- `ManifestDigest`: digest copied from artifact identity.
- `Target`: `DeploymentTargetDescriptor`.
- `Actor`: optional actor id/display metadata.
- `Status`: final status.
- `Plan`: optional plan snapshot.
- `ResourceResults`: ordered per-resource outcomes.
- `Diagnostics`: ordered diagnostics.
- `StartedAt`: timestamp.
- `CompletedAt`: timestamp.

Validation:

- History records are append-oriented.
- Partial failures retain all per-resource outcomes required for reapply-based resume.
