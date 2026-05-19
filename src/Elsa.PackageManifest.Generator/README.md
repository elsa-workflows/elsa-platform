# Elsa.PackageManifest.Generator

`Elsa.PackageManifest.Generator` is a build-time package for Elsa professional runtime packages. Add it to a class library with `PrivateAssets="all"` and it generates an `elsa-package.json` manifest during build/pack.

```xml
<PackageReference Include="Elsa.PackageManifest.Generator" Version="x.y.z" PrivateAssets="all" />
```

The generator inspects the compiled assembly with metadata-only reflection. It discovers public CShells feature classes implementing `CShells.Features.IShellFeature`, reads `ShellFeatureAttribute` metadata, discovers public settable deploy-time feature properties as settings, applies XML documentation and optional manifest hint attributes, validates the result with `Elsa.PackageManifests`, and includes one root `elsa-package.json` in the produced NuGet package.

Delegate-shaped code configuration hooks such as `Action<TOptions>`, `Action<IServiceProvider, HttpClient>`, `Func<IServiceProvider, TService>`, and delegate-valued factory dictionaries are ignored by default because they are application-code extension points rather than deploy-time settings. They do not produce default warnings; set `ElsaPackageManifestDiagnosticsVerbosity=verbose` to see low-importance ignored-hook diagnostics.

Unsupported CLR-only setting shapes such as `System.Type` and complex option objects are also omitted from manifest settings. They produce low-importance diagnostics only, so normal builds and fail-on-warnings builds can continue while the manifest remains limited to deploy-time configurable settings.

Multi-targeted package projects include exactly one canonical manifest by default. When target frameworks produce equivalent manifest surfaces, the first declared target framework supplies the package-root manifest.

`dotnet pack` generates and includes the manifest during the build portion of pack. In build-then-pack pipelines, run `dotnet build` for the same configuration first; `dotnet pack --no-build` reuses the existing `obj/<configuration>/<tfm>/elsa-package.json` instead of regenerating it. If package inclusion is enabled and that manifest is missing during `--no-build`, packing fails with an actionable message telling the maintainer to build first, pack without `--no-build`, or disable manifest package inclusion.

Optional source-only hints are available under `Elsa.PackageManifest.Generator.Hints`:

- `ManifestSettingAttribute`
- `ManifestUIOptionAttribute`
- `ManifestUIOptionsProviderAttribute`
- `ManifestIgnoreAttribute`
- `ManifestExtensionAttribute`
- `ManifestInfrastructureAttribute`

These hint attributes are intentionally shipped as source-only, internal types so consuming packages can use them without exposing generator APIs from their assemblies. `ManifestInfrastructureAttribute.Extensions` uses `key=value` strings; entries without a key before `=` are ignored.

Enum settings emit validation enum values and default to `ui.hint = "select-list"` with static option items. Use `ManifestUIOptionAttribute` for custom static list values and `ManifestUIOptionsProviderAttribute` to reference a trusted Runtime Builder option provider for dynamic list values. Provider references are manifest data only; the generator does not execute package code to resolve options.

For metadata that cannot be inferred, add `elsa-package.overrides.json` beside the project file or set `ElsaPackageManifestOverrideFile`.

Common MSBuild properties:

- `GenerateElsaPackageManifest`
- `ElsaPackageManifestOutputPath`
- `ElsaPackageManifestIncludeInPackage`
- `ElsaPackageManifestValidationSeverity`
- `ElsaPackageManifestStrict`
- `ElsaPackageManifestFailOnWarnings`
- `ElsaPackageManifestAllowTargetFrameworkDifferences`
- `ElsaPackageManifestDiagnosticsVerbosity`
