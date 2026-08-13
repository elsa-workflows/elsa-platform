# Quickstart: Elsa Package Manifest Generator

This quickstart describes how an implementer should validate the first usable
slice once tasks are generated and implemented.

## Prerequisites

- .NET 10 SDK.
- Local shell with `dotnet`.
- A sample class library project that produces a NuGet package.

## Build The Solution

```bash
dotnet restore
dotnet build
```

Expected result:

- Generator projects build.
- Existing catalog and manifest contract tests still compile.

## Run Tests

```bash
dotnet test
```

Expected coverage areas:

- Metadata-only assembly inspection.
- Feature discovery through `CShells.Features.IShellFeature`.
- CShells `ShellFeatureAttribute` metadata and optional manifest hints.
- Setting discovery and exclusions.
- XML documentation enrichment.
- Override file merge and validation.
- JSON Schema Draft 2020-12 setting schema generation.
- Manifest validation through `ValenceControl.PackageManifests`.
- Pack integration and package inspection.
- Deterministic output across repeated builds.
- Safety checks proving constructors and property getters are not invoked.

## Create A Sample Package Project

Create or use a fixture class library that references the generator:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <PackageId>Elsa.Samples.EmailFeature</PackageId>
    <Version>1.0.0</Version>
    <Title>Elsa Sample Email Feature</Title>
    <Description>Sample package for manifest generator validation.</Description>
    <Authors>Elsa</Authors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CShells.Abstractions" Version="x.y.z" />
    <PackageReference Include="ValenceControl.PackageManifest.Generator" Version="x.y.z" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## Add A Feature Class

```csharp
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;

[ShellFeature(
    "Email",
    DisplayName = "Email",
    Description = "Adds email delivery support.")]
public sealed class EmailShellFeature : IShellFeature
{
    /// <summary>
    /// SMTP server host name.
    /// </summary>
    [ManifestSetting(
        DisplayName = "SMTP host",
        Category = "Delivery",
        RestartRequired = true)]
    public string? SmtpHost { get; set; }

    /// <summary>
    /// SMTP server port.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
```

Expected result:

- The generator discovers `EmailShellFeature` because it implements
  `IShellFeature`.
- `ShellFeatureAttribute.Name` produces the CShells feature name `Email`.
- `SmtpHost` is documented with the CShells configuration path
  `Email:SmtpHost`. An equivalent environment variable, when the application
  uses the standard environment variable provider, is naturally
  `Email__SmtpHost`; this does not need a dedicated manifest field.

## Example Generated Manifest

Shape shown for orientation only; the exact JSON members are owned by
`ValenceControl.PackageManifests`.

```json
{
  "schemaVersion": "1.0",
  "package": {
    "id": "Elsa.Samples.EmailFeature",
    "version": "1.0.0",
    "title": "Elsa Sample Email Feature",
    "description": "Sample package for manifest generator validation.",
    "authors": ["Elsa"],
    "targetFrameworks": ["net10.0"]
  },
  "features": [
    {
      "id": "Elsa.Samples.EmailFeature.Email",
      "name": "Email",
      "clrType": "EmailShellFeature",
      "displayName": "Email",
      "description": "Adds email delivery support.",
      "settings": [
        {
          "name": "SmtpHost",
          "configurationPath": "Email:SmtpHost",
          "clrType": "System.String",
          "jsonType": "string",
          "nullable": true,
          "required": false,
          "displayName": "SMTP host",
          "description": "SMTP server host name.",
          "category": "Delivery",
          "restartRequired": true,
          "schema": {
            "type": ["string", "null"]
          }
        },
        {
          "name": "Port",
          "configurationPath": "Email:Port",
          "clrType": "System.Int32",
          "jsonType": "integer",
          "nullable": false,
          "required": true,
          "description": "SMTP server port.",
          "defaultValue": 587,
          "validation": {
            "minimum": 1,
            "maximum": 65535
          },
          "schema": {
            "type": "integer",
            "minimum": 1,
            "maximum": 65535
          }
        }
      ]
    }
  ]
}
```

## Build The Sample

```bash
dotnet build
```

Expected result:

- `obj/{configuration}/{targetframework}/elsa-package.json` is generated.
- Build diagnostics include the generated path and discovered feature count.
- No feature constructors or property getters are invoked.

## Pack The Sample

```bash
dotnet pack
```

Expected result:

- The produced `.nupkg` contains exactly one root `elsa-package.json`.
- The manifest uses the `ValenceControl.PackageManifests` contract.
- The manifest is no larger than 1 MB.

## Validate Override Behavior

Add `elsa-package.overrides.json`:

```json
{
  "package": {
    "documentation": {
      "url": "https://docs.example.com/elsa/email"
    },
    "tags": ["email", "communication"]
  },
  "features": [
    {
      "id": "Elsa.Samples.Email",
      "infrastructure": [
        {
          "id": "smtp",
          "kind": "smtp",
          "optional": false,
          "reason": "Email delivery needs an SMTP-compatible service.",
          "providers": ["smtp", "mailpit"],
          "configurationKeys": ["Email:SmtpHost", "Email:Port"]
        }
      ],
      "settings": [
        {
          "name": "SmtpHost",
          "required": true,
          "uiHint": "text",
          "ui": {
            "hint": "text"
          }
        }
      ]
    }
  ]
}
```

Expected result:

- Override values win over inferred, XML, and attribute metadata.
- Feature infrastructure requirements are emitted as abstract manifest metadata;
  they do not contain Docker Compose fragments or deployment templates.
- Override file references resolve to discovered features and settings.
- Override files larger than 256 KB fail validation.
- Override package ID/version conflicts fail validation.

## Validate Multi-Targeting

Change the fixture to:

```xml
<TargetFrameworks>net10.0;net8.0</TargetFrameworks>
```

Expected result:

- Equivalent feature surfaces produce one canonical package manifest.
- Divergent feature or setting surfaces warn or fail according to configured
  severity.
- The `.nupkg` still contains one root `elsa-package.json` by default.

## Disable Generation

```xml
<GenerateElsaPackageManifest>false</GenerateElsaPackageManifest>
```

Expected result:

- No manifest is generated.
- No manifest is included in the package.

## Inspect Generated Package

Use any NuGet package inspection method to verify:

```text
elsa-package.json
```

Expected result:

- The file is at the package root.
- The manifest package ID/version match the NuGet package ID/version.
- Feature and setting metadata are deterministic across repeated builds.
