# Elsa.Platform.PackageManifest.Generator

`Elsa.Platform.PackageManifest.Generator` is a build-time package for Elsa professional runtime packages. Add it to a class library with `PrivateAssets="all"` and it generates an `elsa-package.json` manifest during build/pack.

```xml
<PackageReference Include="Elsa.Platform.PackageManifest.Generator" Version="x.y.z" PrivateAssets="all" />
```

The generator inspects the compiled assembly with metadata-only reflection. It discovers public CShells feature classes implementing `CShells.Features.IShellFeature`, reads `ShellFeatureAttribute` metadata, discovers public settable deploy-time feature properties as settings, applies XML documentation and optional manifest hint attributes, validates the result with `Elsa.Platform.PackageManifests`, and includes one root `elsa-package.json` in the produced NuGet package.

Delegate-shaped code configuration hooks such as `Action<TOptions>`, `Action<IServiceProvider, HttpClient>`, `Func<IServiceProvider, TService>`, and delegate-valued factory dictionaries are ignored by default because they are application-code extension points rather than deploy-time settings. They do not produce default warnings; set `ElsaPackageManifestDiagnosticsVerbosity=verbose` to see low-importance ignored-hook diagnostics.

Unsupported CLR-only setting shapes such as `System.Type` and complex option objects are also omitted from manifest settings. They produce low-importance diagnostics only, so normal builds and fail-on-warnings builds can continue while the manifest remains limited to deploy-time configurable settings.

Multi-targeted package projects include exactly one canonical manifest by default. When target frameworks produce equivalent manifest surfaces, the first declared target framework supplies the package-root manifest.

`dotnet pack` generates and includes the manifest during the build portion of pack. In build-then-pack pipelines, run `dotnet build` for the same configuration first; `dotnet pack --no-build` reuses the existing `obj/<configuration>/<tfm>/elsa-package.json` instead of regenerating it. If package inclusion is enabled and that manifest is missing during `--no-build`, packing fails with an actionable message telling the maintainer to build first, pack without `--no-build`, or disable manifest package inclusion.

Optional source-only hints are available under `Elsa.Platform.PackageManifest.Generator.Hints`:

- `ManifestFeatureCategoryAttribute`
- `ManifestSettingAttribute`
- `ManifestUIOptionAttribute`
- `ManifestUIOptionsProviderAttribute`
- `ManifestIgnoreAttribute`
- `ManifestExtensionAttribute`
- `ManifestInfrastructureAttribute`
- `ManifestRuntimeKindAttribute`
- `ElsaRuntimeKinds`

These hint attributes are intentionally shipped as source-only, internal types so consuming packages can use them without exposing generator APIs from their assemblies. `ManifestInfrastructureAttribute.Extensions` uses `key=value` strings; entries without a key before `=` are ignored.

Apply `ManifestFeatureCategoryAttribute` zero or more times to a feature class to emit feature manifest categories. The generator also accepts `categories` in `elsa-package.overrides.json`; legacy single `category` override values are still supported.

Enum settings emit validation enum values and default to `ui.hint = "select-list"` with static option items. Use `ManifestUIOptionAttribute` for custom static list values and `ManifestUIOptionsProviderAttribute` to reference a trusted Runtime Builder option provider for dynamic list values. Provider references are manifest data only; the generator does not execute package code to resolve options.

For metadata that cannot be inferred, add `elsa-package.overrides.json` beside the project file or set `ElsaPackageManifestOverrideFile`.

Runtime kind compatibility can be declared through overrides:

```json
{
  "package": {
    "compatibility": {
      "runtimeKinds": [ "elsa.server" ]
    }
  },
  "features": [
    {
      "id": "My.Package.StudioWidget",
      "compatibility": {
        "runtimeKinds": [ "elsa.studio" ]
      }
    }
  ]
}
```

Use `elsa.server` for Elsa Server packages and `elsa.studio` for Elsa Studio packages. Feature-level compatibility narrows or specializes the package-level compatibility for that feature.

For the common case, prefer source-only attributes in package code:

```csharp
using Elsa.Platform.PackageManifest.Generator.Hints;

[assembly: ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
```

Feature-level attributes apply compatibility to the individual feature:

```csharp
[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ShellFeature("StudioWidget", DisplayName = "Studio Widget")]
public sealed class StudioWidgetFeature : IShellFeature
{
}
```

Override files remain useful for CI- or packaging-specific metadata. When `runtimeKinds` are supplied in an override file, they take precedence over attribute-provided runtime kinds for that package or feature.

Common MSBuild properties:

- `GenerateElsaPackageManifest`
- `ElsaPackageManifestOutputPath`
- `ElsaPackageManifestIncludeInPackage`
- `ElsaPackageManifestValidationSeverity`
- `ElsaPackageManifestStrict`
- `ElsaPackageManifestFailOnWarnings`
- `ElsaPackageManifestAllowTargetFrameworkDifferences`
- `ElsaPackageManifestDiagnosticsVerbosity`
