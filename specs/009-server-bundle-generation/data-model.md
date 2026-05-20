# Data Model: Server-Side Bundle Generation

## RuntimeBuilderIntent

Represents the complete desired runtime shape submitted for bundle generation.

Fields:

- `Image`: selected runtime image and image-level overrides.
- `Packages`: selected source-qualified package versions and selected feature IDs.
- `PackageSources`: explicit package source selections used by generated feed configuration.
- `Infrastructure`: selected infrastructure providers and provider settings.
- `LocalPackages`: optional local package folder behavior.

Relationships:

- Contains one `RuntimeImageSelection`.
- Contains many `BundlePackageSelection` records.
- Contains zero or more `PackageSourceSelection` records.
- Contains zero or more `InfrastructureSelection` records.

Validation:

- Image slug is required and must resolve to known runtime image metadata.
- Package selections require `sourceId`, `packageId`, and `version`.
- Selected feature IDs must exist in selected package manifests.
- Package sources must be explicit and visible to the caller.
- Infrastructure providers must be known and compatible with selected requirements.

## RuntimeImageSelection

The selected Docker runtime image and user-provided image overrides.

Fields:

- `Slug`: stable runtime image identifier.
- `Tag`: selected image tag; defaults from image metadata when omitted.
- `HostPort`: optional host port override.
- `EnvOverrides`: user-provided environment variable values.

Validation:

- Slug must not be empty.
- Tag must be known or accepted by the runtime image metadata policy.
- Host port, when provided, must be a valid TCP port.
- Secret override values must not be written to logs or findings.

## BundlePackageSelection

A source-qualified package version selected for the runtime.

Fields:

- `SourceId`: package source identifier.
- `PackageId`: NuGet package ID.
- `Version`: selected package version.
- `SelectedFeatures`: feature IDs enabled from that package.
- `Settings`: feature setting values keyed by feature and setting name.

Relationships:

- Resolves to one visible `PackageVersion` in the existing catalog database.
- Reads existing manifest features and settings from the selected version.

Validation:

- Source ID, package ID, and version are required.
- Package version must be visible to the caller, valid, approved, listed, and non-suspicious.
- Settings for selected features must satisfy required setting/default rules before rendering.

## PackageSourceSelection

Package source metadata included in generated feed and lock files.

Fields:

- `SourceId`: source identifier.
- `Name`: optional display name supplied by client or resolved from catalog.
- `Url`: sanitized source URL used for generated configuration.
- `Kind`: package source kind when needed by generated output.

Validation:

- Source must be catalog-owned public and browseable for trusted public bundle generation, or visible to the selected workspace for workspace bundle generation.
- Credentials, query strings, and fragments are not exposed in generated non-secret files.
- Arbitrary unindexed feed URLs are not accepted in this feature.

## InfrastructureSelection

The selected provider that satisfies a runtime infrastructure requirement.

Fields:

- `Kind`: infrastructure kind, such as `database` or `message-broker`.
- `ProviderId`: selected provider identifier.
- `Strategy`: selected provider strategy, such as `compose-sidecar` or `external-service`.
- `Settings`: provider-specific setting values.

Relationships:

- Must match an `InfrastructureProvider` from the existing provider catalog.
- May satisfy requirements declared by selected package feature manifests.

Validation:

- Provider ID and kind are required.
- Provider strategy must be supported by first-release Docker Compose generation.
- Required provider outputs must be materializable as generated configuration or placeholders.

## LocalPackagesOptions

Controls optional local package folder behavior in generated files.

Fields:

- `Enabled`: whether local packages are included.
- `DirectoryPath`: relative local package folder path.

Validation:

- Directory path is required when enabled.
- Directory path must be relative and must not escape the generated bundle root.

## BundleGenerationResult

The response produced by bundle generation.

Fields:

- `BundleId`: ephemeral identifier or `preview` marker.
- `Files`: generated bundle files. Empty when blocking findings are present.
- `Findings`: errors, warnings, and informational findings.

Validation:

- Blocking error findings require an empty file list.
- Warning-only findings may be returned with generated files.
- File ordering is deterministic.

## BundleFile

A generated text artifact.

Fields:

- `Path`: stable relative path within the bundle.
- `Language`: frontend preview language hint.
- `ContentType`: file media type.
- `Contents`: generated text.
- `Required`: whether the file is required for the bundle contract.

Validation:

- Path must be relative and must not contain parent-directory traversal.
- Required first-release files are `config.json`, `packages.lock.json`, `docker-compose.yml`, `.env.example`, and `README.md`.
- `Program.Generated.cs` may be present only as optional reference output.

## BundleFinding

A structured issue or recommendation returned by generation.

Fields:

- `Level`: `error`, `warning`, or `info`.
- `Code`: stable machine-readable code.
- `Message`: user-facing explanation.
- `Scope`: optional scope such as image, package, feature, infrastructure, setting, or file.

Validation:

- Error findings are blocking when they make generated output unsafe or misleading.
- Finding messages must not include secret values.
- Codes must be stable enough for clients to branch on.

## GenerationDiagnostic

Non-secret operational metadata about a generation attempt.

Fields:

- `Outcome`: success, warning, or blocked.
- `Duration`: generation duration.
- `SelectedImageSlug`: selected image slug.
- `PackageCount`: number of selected packages.
- `FeatureCount`: number of selected features.
- `InfrastructureCount`: number of selected infrastructure providers.
- `GeneratedFileCount`: number of returned files.
- `FindingCounts`: counts by finding level.

Validation:

- Must not include generated file contents.
- Must not include secret setting values, private feed credentials, or raw unsanitized URLs.
- May be logged or emitted through diagnostics, but no durable storage is required for this feature.

## State Transitions

Successful generation:

```text
trusted request -> normalize intent -> validate source/package/image/infrastructure/settings -> render required files -> return files and findings -> discard file contents
```

Warning-only generation:

```text
trusted request -> normalize intent -> validate with warnings -> render required files -> return files and warnings -> discard file contents
```

Blocked generation:

```text
trusted request -> normalize intent -> validate with blocking errors -> return findings only -> no files rendered or returned
```
