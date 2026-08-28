# Data Model: Deployment Manifest Parsing

## EnvironmentManifest

- `ApiVersion`: must be `elsa-control/v1alpha1`.
- `Kind`: must be `EnvironmentManifest`.
- `Metadata`: `ManifestMetadata`.
- `Resources`: `ManifestResources`.

Validation:

- `ApiVersion`, `Kind`, and `Metadata.Name` are required.
- Unsupported versions and kinds produce diagnostics.

## ManifestMetadata

- `Name`: environment manifest name.
- `Version`: optional manifest version.
- `Environment`: optional environment label.
- `Labels`: string dictionary.
- `Annotations`: string dictionary.

Validation:

- Name is required and trimmed.
- Labels and annotations must contain string keys and safe string values.

## ManifestResources

- `Workflows`: workflow entries.
- `Variables`: variable entries.
- `Features`: feature descriptor entries.
- `Packages`: package descriptor entries.
- `Recipes`: recipe descriptor entries.
- `Extensions`: mapped custom sections.

Validation:

- Empty resource collections are allowed but produce no resources.
- Unknown sections without a registered mapper produce diagnostics.

## WorkflowManifestEntry

- `Id`: required workflow definition id.
- `Path`: required manifest-relative workflow definition path.
- `Activation`: optional activation state.
- `Version`: optional desired version/revision.
- `Dependencies`: optional deployment resource dependencies.
- `Metadata`: string dictionary.

Normalized type: `workflowDefinition`.

## VariableManifestEntry

- `Key`: required variable key.
- `Value`: scalar or structured JSON-compatible value.
- `Scope`: optional scope.
- `Dependencies`: optional deployment resource dependencies.
- `Metadata`: string dictionary.

Normalized type: `variable`.

## FeatureManifestEntry

- `Id`: required feature id.
- `State`: enabled or disabled descriptor state.
- `Dependencies`: optional dependencies.
- `Metadata`: string dictionary.

Normalized type: `feature`.

## PackageManifestEntry

- `Id`: required package id.
- `Version`: optional exact version or range.
- `Dependencies`: optional dependencies.
- `Metadata`: string dictionary.

Normalized type: `package`.

## RecipeManifestEntry

- `Id`: required recipe id.
- `Path`: optional manifest-relative recipe path.
- `Version`: optional version.
- `Dependencies`: optional dependencies.
- `Metadata`: string dictionary.

Normalized type: `recipe`.

## NormalizedManifest

- `Manifest`: parsed environment manifest.
- `Resources`: ordered `DeploymentResource` values.
- `Diagnostics`: ordered `DeploymentDiagnostic` values.

Validation:

- Duplicate `DeploymentResourceId` values produce diagnostics.
- Desired-state hashes are deterministic across equivalent YAML/JSON.
