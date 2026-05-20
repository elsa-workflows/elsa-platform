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

Validation:

- Slug and image reference are required.
- Slugs are unique.
- Default tag is included in available tags.
- Companion image slug, when set, references another image.

## RuntimeImageEnvironmentVariable

Fields:

- `Name`, `DisplayName`, `Description`
- `Required`, `Secret`, `Advanced`
- `DefaultValue`
- `Group`

Validation:

- Names are unique per image.
- Secret defaults are not exposed unless they are safe placeholders.

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
