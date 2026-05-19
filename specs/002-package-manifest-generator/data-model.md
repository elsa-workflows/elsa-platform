# Data Model: Elsa Package Manifest Generator

## Overview

The generator does not introduce durable application storage. Its model is a
build-time data flow from project metadata, assembly metadata, XML documentation,
CShells metadata, optional manifest hints, and optional override JSON into a validated
`Elsa.Platform.PackageManifests` manifest object and a generated `elsa-package.json`
artifact.

All model objects are deterministic value inputs, intermediate projections, or
outputs. They must be safe to construct without executing package code.

## Value Objects

### GeneratorOptions

Build-time configuration resolved from MSBuild properties.

Fields:

- `GenerateManifest`: default `true`
- `OutputPath`: intermediate manifest path
- `IncludeInPackage`: default `true`
- `PackagePath`: default `elsa-package.json`
- `OverrideFile`: optional path, default `elsa-package.overrides.json` when present
- `ValidationSeverity`: `Error`, `Warning`, or `None` where safe
- `Strict`: default `false`
- `FailOnWarnings`: default `false`
- `AllowTargetFrameworkDifferences`: default `false`
- `DiagnosticsVerbosity`: concise by default
- `AdditionalFeatureInterfaceTypes`: advanced extra feature marker interfaces, default empty

Validation:

- `PackagePath` defaults to root `elsa-package.json`.
- `OverrideFile`, when present, must be no larger than 256 KB.
- Output paths must stay under the project intermediate output location unless
  explicitly overridden.

### ProjectPackageMetadata

Package-level metadata inferred from project and NuGet properties.

Fields:

- `PackageId`
- `Version`
- `Title`
- `Description`
- `Authors`
- `RepositoryUrl`
- `PackageProjectUrl`
- `PackageTags`
- `PackageLicenseExpression`
- `PackageReadmeFile`
- `TargetFramework`
- `TargetFrameworks`

Validation:

- `PackageId` and `Version` are required for package inclusion.
- Package display name defaults to `Title` when it is distinct from `PackageId`; otherwise it omits the standard `Elsa.` namespace prefix from the package ID.
- Override files cannot change `PackageId` or `Version`.
- Target frameworks are ordered deterministically.

### AssemblyInspectionInput

Metadata-only assembly inspection inputs.

Fields:

- `AssemblyPath`
- `XmlDocumentationPath`
- `TargetFramework`
- `ReferenceAssemblyPaths`
- `NullableMetadataAvailable`

Validation:

- Assembly path is required when generation is enabled.
- Missing XML documentation is allowed but may produce warnings.
- Reference assemblies are used only for metadata identity resolution.

### DiscoveredFeature

Intermediate projection of a CShells feature type.

Fields:

- `FeatureId`
- `CShellsFeatureName`
- `ClrTypeName`
- `DisplayName`
- `Description`
- `Category`
- `DiscoverySource`: `IShellFeature`, `InheritedIShellFeature`
- `IsPublic`
- `IsAbstract`
- `IsGenericDefinition`
- `Advanced`
- `Experimental`
- `Dependencies`
- `Conflicts`
- `RequiredCapabilities`
- `ExtensionMetadata`
- `Settings`

Validation:

- `FeatureId` is unique within the package manifest.
- Abstract and generic type definitions are excluded.
- Internal types are excluded unless explicitly included.
- `ShellFeatureAttribute` metadata wins over ambiguous convention metadata.

### DiscoveredSetting

Intermediate projection of a configurable feature property.

Fields:

- `FeatureId`
- `Name`
- `ClrPropertyName`
- `ClrType`
- `JsonType`
- `ConfigurationPath`
- `Required`
- `Nullable`
- `DefaultValue`
- `DisplayName`
- `Description`
- `Category`
- `Group`
- `ValidationConstraints`
- `EnumValues`
- `Secret`
- `Sensitive`
- `RestartRequired`
- `UIHint`
- `UIOptions`
- `UIOptionsProvider`
- `Advanced`
- `Experimental`
- `ExtensionMetadata`

Validation:

- Setting names are unique within a feature.
- Public instance properties with public setters are included by default.
- Static, indexer, ignored, computed-only, and read-only properties are excluded
  unless explicitly included by supported metadata.
- `ConfigurationPath` is derived from the CShells binding convention:
  `{CShellsFeatureName}:{ClrPropertyName}`.
- Non-nullable Boolean settings default to optional with `DefaultValue = false`
  unless explicit default or required metadata overrides that inference.
- Complex object settings are unsupported in the MVP unless represented as a
  supported primitive, enum, nullable, array, list, or dictionary shape.
- Enum settings publish enum values as validation metadata and default to a
  `select-list` UI hint with static option items.
- Dynamic UI option values are represented by provider IDs and parameters only;
  package code must not be executed to resolve them.

### ManifestUIOptionReference

Static UI option metadata for a setting.

Fields:

- `Value`
- `Label`
- `Description`

Validation:

- `Value` is required.
- Options are ordered deterministically.

### ManifestUIOptionsProviderReference

Dynamic UI option metadata resolved by a trusted Runtime Builder client or
runtime service.

Fields:

- `Provider`: stable provider ID
- `DependsOn`: setting names whose values influence option resolution
- `Parameters`: simple provider parameters

Validation:

- `Provider` is required when dynamic options are declared.
- Provider references are data only; generation and catalog ingestion must not
  execute package assemblies to resolve option values.

### XmlDocumentationEntry

Documentation extracted from XML documentation files.

Fields:

- `MemberName`: XML doc member identifier
- `Summary`
- `Remarks`
- `Examples`

Validation:

- Missing entries do not stop generation.
- Summaries populate descriptions only when no higher-priority source supplies a
  value.

### ManifestOverride

Optional JSON file used to enrich or override inferred metadata.

Fields:

- `Package`
- `Documentation`
- `Icon`
- `Tags`
- `Compatibility`
- `License`
- `Dependencies`
- `Conflicts`
- `RequiredCapabilities`
- `Features`
- `Settings`
- `Extensions`

Validation:

- File size must be no greater than 256 KB.
- JSON must match the override schema.
- Package ID/version conflicts with NuGet metadata are validation errors.
- Feature references resolve by feature ID first, then CLR type name.
- Setting references resolve by feature ID plus setting name.
- Unknown fields are allowed only in extension metadata locations.

### GeneratedSettingsSchema

JSON Schema metadata for a feature setting.

Fields:

- `Type`
- `Format`
- `Nullable`
- `Required`
- `Enum`
- `Items`
- `AdditionalProperties`
- `Constraints`
- `Description`
- `Default`
- `Extensions`

Validation:

- Generated schema fragments follow JSON Schema Draft 2020-12.
- Enum value ordering is deterministic.
- Unsupported CLR types produce configured diagnostics.

### InfrastructureRequirementManifest

Abstract infrastructure dependency declared by a feature.

Fields:

- `Id`: stable requirement identifier within the declaring feature
- `Kind`: abstract kind such as `database`, `message-broker`, `cache`,
  `blob-storage`, `smtp`, or `secret-store`
- `Optional`: whether the feature can run without the dependency
- `Reason`: human-readable explanation
- `Capabilities`: abstract capability tags expected from a provider
- `Providers`: provider hints such as `postgres`, `rabbitmq`, or
  `azure-service-bus`
- `ConfigurationKeys`: CShells/IConfiguration paths that can be bound from the
  selected infrastructure provider
- `Extensions`: future metadata

Validation:

- `Id` and `Kind` are required when a requirement is declared.
- Requirements are declarative and must not include deployment fragments.
- Override files are the MVP source for infrastructure requirements; inference
  from package references is deferred.

### GenerationDiagnostic

Build diagnostic produced by the generator.

Fields:

- `Code`
- `Severity`: `Info`, `Warning`, `Error`
- `Message`
- `Target`: project, type, property, manifest path, or override path
- `ManifestPath`
- `RuleId`

Validation:

- Diagnostics must be stable and actionable.
- Secret values must not be printed.
- Default verbosity avoids per-property success logs.

### GeneratedManifestArtifact

Output artifact written by the generator.

Fields:

- `IntermediatePath`
- `PackagePath`
- `ManifestJson`
- `ManifestSize`
- `ValidationResult`
- `IncludedInPackage`

Validation:

- `ManifestSize` must be no greater than 1 MB.
- The package contains one canonical root `elsa-package.json` by default.
- JSON ordering is deterministic.

## State Transitions

### Single-Target Project

1. `GeneratorOptions` and `ProjectPackageMetadata` are resolved.
2. Assembly metadata and XML docs are inspected.
3. Features and settings are discovered.
4. XML documentation, CShells metadata, and manifest hints enrich discovered metadata.
5. Override file is validated and merged.
6. `Elsa.Platform.PackageManifests` object is built.
7. Manifest is schema validated.
8. `elsa-package.json` is written to the intermediate path.
9. Pack includes the manifest at the package root.

### Multi-Target Project

1. Per-target intermediate manifests are generated for comparison.
2. Manifest-relevant feature and setting surfaces are normalized.
3. Equivalent surfaces produce one canonical package manifest.
4. Divergent surfaces produce warning/error diagnostics unless explicitly
   allowed.
5. Pack includes only one canonical root manifest by default.

## Relationships

- `GeneratorOptions` controls all generation behavior.
- `ProjectPackageMetadata` supplies package-level manifest fields.
- `AssemblyInspectionInput` produces `DiscoveredFeature` records.
- `DiscoveredFeature` owns `DiscoveredSetting` records.
- `XmlDocumentationEntry` enriches features and settings before CShells metadata,
  manifest hints, and overrides.
- `ManifestOverride` has highest precedence for allowed metadata.
- `GeneratedSettingsSchema` is attached to each setting manifest.
- `GeneratedManifestArtifact` contains the final contract JSON and validation
  result.
