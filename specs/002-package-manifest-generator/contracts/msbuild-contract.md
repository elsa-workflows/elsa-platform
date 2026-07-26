# Contract: MSBuild Integration

## Package Reference

Consuming projects use one private package reference:

```xml
<PackageReference Include="ValenceControl.PackageManifest.Generator" Version="x.y.z" PrivateAssets="all" />
```

No additional targets, props, content items, or package item configuration is
required for the default workflow.

## Default Behavior

- Generation is enabled by default.
- Generation runs after compilation has produced the assembly and XML
  documentation, and before NuGet pack finalizes package contents.
- Direct `dotnet pack` triggers generation when a separate build was not run
  first.
- `dotnet pack --no-build` reuses an existing intermediate manifest instead of
  rerunning generation. If package inclusion is enabled and the manifest is
  missing, pack fails with an actionable build-first message.
- The intermediate manifest path defaults to
  `$(IntermediateOutputPath)elsa-package.json` for the active target framework.
- The NuGet package path defaults to root `elsa-package.json`.
- Multi-targeted packages include exactly one canonical root manifest by default.
  When target frameworks produce equivalent manifest surfaces, the first
  declared target framework supplies the canonical package manifest.

## Properties

| Property | Default | Description |
|----------|---------|-------------|
| `GenerateElsaPackageManifest` | `true` | Enables or disables generation. |
| `ElsaPackageManifestOutputPath` | `$(IntermediateOutputPath)elsa-package.json` | Overrides the generated intermediate file path. |
| `ElsaPackageManifestIncludeInPackage` | `true` | Includes the generated file in the NuGet package. |
| `ElsaPackageManifestPackagePath` | `elsa-package.json` | Overrides the package path. |
| `ElsaPackageManifestOverrideFile` | `elsa-package.overrides.json` when present | Sets the override file path. |
| `ElsaPackageManifestValidationSeverity` | `Error` | Controls required schema validation severity where safe. |
| `ElsaPackageManifestStrict` | `false` | Enables stricter recommended metadata checks. |
| `ElsaPackageManifestFailOnWarnings` | `false` | Treats warnings as build failures. |
| `ElsaPackageManifestAllowTargetFrameworkDifferences` | `false` | Allows target-specific manifest surface differences. |
| `ElsaPackageManifestDiagnosticsVerbosity` | `concise` | Controls diagnostic verbosity. |
| `ElsaPackageManifestAdditionalFeatureInterfaceTypes` | empty | Advanced semicolon-separated additional marker interfaces for CShells-compatible feature discovery. |

## Items Passed To The Task

- Compiled assembly path.
- XML documentation path when available.
- Target framework.
- Target frameworks list for package metadata.
- Reference assembly paths needed for metadata identity resolution.
- Project and NuGet package metadata.
- Override file path when configured or present.

## Diagnostics

Default diagnostics include:

- Generated manifest path.
- Package inclusion path.
- Discovered feature count.
- Missing XML documentation when configured.
- Unsupported feature or setting patterns.
- Unsupported property types.
- Invalid override file structure.
- Override references to missing features/settings.
- Schema validation errors with manifest paths.
- Multi-targeting differences.

The task must avoid noisy per-property success logs unless verbose diagnostics
are enabled.

## Failure Behavior

Build fails by default when:

- Required assembly metadata cannot be inspected.
- Override JSON is malformed or larger than 256 KB.
- Override package ID/version conflicts with NuGet metadata.
- The generated manifest violates the required manifest schema.
- The generated manifest is larger than 1 MB.
- Multi-targeted feature surfaces diverge and differences are not allowed.

Warnings are emitted by default when:

- XML documentation is missing and configured policy expects descriptions.
- Recommended metadata such as descriptions or documentation links is missing.
- Unsupported optional metadata is ignored.
- Delegate-shaped code configuration hooks are ignored as low-importance or
  verbose diagnostics only.
- Unsupported setting properties are omitted as low-importance diagnostics only.

`ElsaPackageManifestFailOnWarnings=true` turns warnings into build failures.
`ElsaPackageManifestValidationSeverity=Warning` maps manifest validation errors
to warnings, but infrastructure and invalid input failures still fail.
