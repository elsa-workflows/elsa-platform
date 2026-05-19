# Data Model: Generator Adoption Fixes for Elsa Shell Modules

This feature refines existing generator concepts rather than introducing a new
manifest contract.

## Generator Diagnostic

Represents a finding emitted during manifest generation, validation, or task
execution.

Fields:

- `Code`: Stable diagnostic code.
- `Message`: Actionable diagnostic text.
- `Severity`: Info, warning, or error before MSBuild logging.
- `Category`: Manifest validation, recommended metadata, setting discovery,
  package inclusion, infrastructure, or invalid input.
- `Path`: Optional manifest path, type name, property name, or package path.
- `CanMapValidationSeverity`: True only for manifest validation findings that
  may follow `ElsaPackageManifestValidationSeverity`.
- `IsFatal`: True for infrastructure and invalid input failures that must fail
  regardless of validation severity.

Validation rules:

- Fatal diagnostics always make the task fail.
- Manifest validation diagnostics may be mapped to warnings when validation
  severity is warning.
- Warning diagnostics make the task fail only when fail-on-warnings is enabled.
- The task must not return failure when only non-fatal warnings were logged.

## Generator Adoption Policy

Represents the effective build behavior for diagnostics.

Fields:

- `ValidationSeverity`: Error, Warning, or None according to existing generator
  policy.
- `FailOnWarnings`: Whether warnings should fail the task.
- `Strict`: Whether recommended metadata validation is stricter.
- `DiagnosticsVerbosity`: Concise or verbose diagnostic output.

Validation rules:

- Warning severity maps manifest validation errors to warnings.
- Warning severity does not map infrastructure or invalid input failures.
- Fail-on-warnings applies after severity mapping.

## Discovered Setting Candidate

Represents a public settable property found on a discovered shell feature before
it becomes a deploy-time setting.

Fields:

- `FeatureId`: Owning feature identifier.
- `Name`: Property name.
- `ClrType`: Property CLR type name.
- `Shape`: Supported setting, unsupported setting omitted, ignored code hook,
  ignored by metadata, or excluded member.
- `ContainerShape`: None, array, enumerable, list, dictionary, read-only
  dictionary, or nested generic container.
- `ElementOrValueShape`: Supported value, unsupported value, or delegate-shaped
  code hook.
- `DiagnosticVisibility`: None, low-importance, concise, or verbose.

Validation rules:

- Direct delegate-shaped candidates become ignored code hooks.
- Candidates whose collection element or dictionary value is delegate-shaped
  become ignored code hooks.
- Ignored code hooks are excluded before setting schema generation.
- Ignored code hooks do not emit default warnings.
- Non-delegate unsupported candidates are excluded from manifest settings.
- Non-delegate unsupported candidates emit low-importance non-warning
  diagnostics with feature, property, and CLR type context.

## Code Configuration Hook

Represents a property that configures behavior through application code rather
than deploy-time configuration.

Fields:

- `FeatureId`: Owning feature identifier.
- `PropertyName`: Property name.
- `HookShape`: Direct delegate, action callback, factory callback, collection of
  delegates, dictionary of delegate values, or nested delegate container.
- `Reason`: Ignored because it cannot be represented as deploy-time
  configuration.

Validation rules:

- Must not appear in generated manifest settings.
- Must not create unsupported-setting errors.
- May appear in verbose diagnostics only.

## Unsupported Setting Candidate

Represents a public settable property that looks configurable by shape but
cannot be represented by the current manifest setting contract.

Fields:

- `FeatureId`: Owning feature identifier.
- `PropertyName`: Property name.
- `ClrType`: Unsupported CLR type name such as `System.Type` or a complex
  options object.
- `Reason`: Omitted because the shape has no supported deploy-time schema.

Validation rules:

- Must not appear in generated manifest settings.
- Must not create warning or error diagnostics by default.
- Must emit a low-importance diagnostic that allows maintainers to understand
  why the property is absent.

## Target Framework Manifest Surface

Represents the manifest-relevant output for one target framework before package
inclusion.

Fields:

- `TargetFramework`: Target framework moniker.
- `DeclaredOrder`: Position in the consuming project's declared target framework
  list.
- `ManifestPath`: Intermediate manifest path.
- `SurfaceFingerprint`: Deterministic comparison value for features and settings.
- `IsCanonical`: Whether this target framework supplies the root package
  manifest.

Validation rules:

- Equivalent surfaces choose the first declared target framework as canonical.
- Divergent surfaces produce diagnostics according to configured severity.
- Exactly one canonical root package manifest is included by default.

## Canonical Package Manifest

Represents the single package-root manifest included in a produced NuGet package.

Fields:

- `SourceTargetFramework`: Target framework that supplied the manifest.
- `IntermediatePath`: Source file path under the intermediate output directory.
- `PackagePath`: Package path, defaulting to `elsa-package.json`.
- `IncludeInPackage`: Whether package inclusion is enabled.

Validation rules:

- Default package output contains exactly one root `elsa-package.json`.
- Custom package path is honored when configured.
- Direct pack must generate and include the canonical manifest.
