# Quickstart: Generator Adoption Fixes for Elsa Shell Modules

This quickstart describes the verification scenarios for the adoption hardening
work.

## 1. Add The Generator To A Multi-Target Module

Use a representative Elsa shell-feature package project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
    <PackageId>Sample.Elsa.Module</PackageId>
    <Version>1.0.0</Version>
    <Description>Sample Elsa module.</Description>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ValenceControl.PackageManifest.Generator" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

No custom manifest items, pack targets, or
`TargetsForTfmSpecificContentInPackage` workaround should be present.

## 2. Include Shell Feature Code Hooks

The sample shell feature should include normal deploy-time settings and code
configuration hooks:

```csharp
using CShells.Features;

[ShellFeature("Sample")]
public sealed class SampleShellFeature : IShellFeature
{
    public string? Endpoint { get; set; }

    public Action<SampleOptions>? Configure { get; set; }

    public Func<IServiceProvider, object>? ServiceFactory { get; set; }

    public Action<IServiceProvider, HttpClient>? ConfigureHttpClient { get; set; }

    public IDictionary<string, Func<IServiceProvider, ValueTask<object>>> Factories { get; set; } =
        new Dictionary<string, Func<IServiceProvider, ValueTask<object>>>();
}
```

Expected result:

- `Endpoint` appears as a manifest setting.
- Delegate-shaped hooks do not appear as manifest settings.
- Delegate-shaped hooks do not produce default warnings or unsupported-setting
  errors.

## 3. Build With Warning Severity

Run:

```bash
dotnet build Sample.Elsa.Module.csproj \
  -p:ElsaPackageManifestValidationSeverity=Warning \
  -p:ElsaPackageManifestFailOnWarnings=false
```

Expected result:

- Build succeeds when only mapped manifest validation warnings are present.
- Warning diagnostics are logged as warnings.
- The build does not report `MSB4181` for a false task return without an error.
- Infrastructure or invalid input failures still fail.

Then run:

```bash
dotnet build Sample.Elsa.Module.csproj \
  -p:ElsaPackageManifestValidationSeverity=Warning \
  -p:ElsaPackageManifestFailOnWarnings=true
```

Expected result:

- Build fails when mapped warnings are present.
- Diagnostics explain the underlying warning findings.

## 4. Pack Without Custom Targets

For build-then-pack pipelines, run build first and then pack without rebuilding:

```bash
dotnet build Sample.Elsa.Module.csproj --configuration Release
dotnet pack Sample.Elsa.Module.csproj --no-build
```

If testing direct pack behavior, run without a prior explicit build:

```bash
dotnet pack Sample.Elsa.Module.csproj
```

Expected result:

- The produced package contains exactly one package entry named
  `elsa-package.json`.
- `dotnet pack --no-build` reuses the manifest from the prior build and does not
  rerun manifest generation.
- `dotnet pack --no-build` fails clearly if the manifest is missing.
- The canonical manifest source is the first declared target framework when
  target frameworks have equivalent manifest surfaces.
- No consumer-side pack workaround is needed.

## 5. Regression Commands

Run focused tests while implementing:

```bash
dotnet test tests/ValenceControl.PackageManifest.Generator.Core.Tests/ValenceControl.PackageManifest.Generator.Core.Tests.csproj
dotnet test tests/ValenceControl.PackageManifest.Generator.MSBuild.Tests/ValenceControl.PackageManifest.Generator.MSBuild.Tests.csproj
dotnet test tests/ValenceControl.PackageManifest.Generator.IntegrationTests/ValenceControl.PackageManifest.Generator.IntegrationTests.csproj
```

Run the full solution before completion:

```bash
dotnet test ValenceControl.sln
```
