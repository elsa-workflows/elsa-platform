# Data Model: Runtime Image Metadata API

## RuntimeImage

Fields:

- `Slug`: stable builder identifier.
- `DisplayName`: user-visible name.
- `Description`: concise builder-facing description.
- `Image`: Docker image reference.
- `AvailableTags`: curated tags.
- `DefaultTag`: selected when the client omits a tag.
- `DefaultPort`, `HostPort`: container and suggested host ports.
- `ContainerName`: suggested Compose container name.
- `LicenseTier`, `Stability`: catalog classifications.
- `Capabilities`: runtime capabilities such as `server` or `studio`.
- `EnvVars`: environment variable definitions.
- `DeploymentHints`: deployment-affecting behavior.
- `Docs`: optional links and container path references.
- `LifecycleState`: active, hidden, deprecated, disabled, removed, or equivalent lifecycle state once configurable catalogs are introduced.
- `SupersededBySlug`: optional replacement image slug when an image is deprecated or superseded.

Validation:

- Slug and image reference are required.
- Slugs are unique.
- Default tag is included in available tags.
- Companion image slug, when set, references another image.
- Hidden, deprecated, disabled, or removed images remain diagnosable when referenced by saved configurations.

## RuntimeImageEnvironmentVariable

Fields:

- `Name`, `DisplayName`, `Description`
- `Required`, `Secret`, `Advanced`
- `DefaultValue`
- `Group`
- `LifecycleState`: active, deprecated, disabled, removed, or equivalent lifecycle state once configurable catalogs are introduced.
- `SupersededByName`: optional replacement variable name when an attribute is renamed or superseded.

Validation:

- Names are unique per image.
- Secret defaults are not exposed unless they are safe placeholders.
- Removed or renamed attributes remain diagnosable when referenced by saved configurations.

## RuntimeImageDeploymentHints

Fields:

- `SupportsDockerCompose`
- `SupportsKubernetes`
- `RequiresCompanionServer`
- `CompanionImageSlug`
- `NeedsSharedNetwork`

Validation:

- Companion fields are internally consistent.
- First slice must support Docker Compose for all known images.

## RuntimeImageDocs

Fields:

- `DockerHubUrl`
- `ContainerPaths`
- `ShowPerShellAdmin`
- `ShowNuplane`

Validation:

- Docs fields must not affect generated deployment output.

## RuntimeImageCatalogSource

Fields:

- `SourceKind`: source-controlled seed, appsettings, operator API, persisted admin record, or other backend-owned source.
- `Scope`: global, organization, workspace, entitlement-filtered, or deployment-target-filtered visibility.
- `Definitions`: runtime image definitions loaded from the source.
- `ValidationFindings`: catalog-source validation results.

Validation:

- Source data is validated before it becomes authoritative for builder catalog, planning, or bundle generation.
- Frontend code consumes the normalized catalog response and does not own deployment-affecting image options or defaults.
