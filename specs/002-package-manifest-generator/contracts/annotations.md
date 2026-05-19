# Contract: CShells Metadata And Manifest Hints

## CShells Feature Metadata

Feature discovery is based on the CShells runtime contract, not a generator-owned
feature attribute.

The generator discovers concrete exposed types assignable to:

```csharp
CShells.Features.IShellFeature
```

When present, the generator reads:

```csharp
CShells.Features.ShellFeatureAttribute
```

Supported metadata:

- `Name`
- `DisplayName`
- `Description`
- `DependsOn`
- `Metadata`

Rules:

- `ShellFeatureAttribute` enriches a discovered `IShellFeature`; it does not make
  a non-feature type discoverable by itself.
- `Name` is the CShells feature name and the configuration section name.
- When `Name` is absent, derive the feature name using the CShells convention by
  stripping `ShellFeature` or `Feature` suffixes from the CLR type name.
- Feature setting configuration paths are derived as
  `{CShellsFeatureName}:{PropertyName}`.
- Environment variable names are not stored as first-class manifest metadata.
  Environment variables already flow through `IConfiguration`.

## Optional Manifest Hints

Generator-owned hints are compile-time inputs only. They must not replace
`CShells.Features.ShellFeatureAttribute` and must not create a separate manifest
contract from `Elsa.PackageManifests`.

If included in the MVP, source-only hint attributes are emitted into:

```csharp
namespace Elsa.PackageManifest.Generator.Hints;
```

## ManifestSettingAttribute

Applies to public feature setting properties.

Supported metadata:

- `DisplayName`
- `Description`
- `Category`
- `Group`
- `Required`
- `DefaultValue`
- `UIHint`
- `Secret`
- `Sensitive`
- `RestartRequired`
- `Advanced`
- `Experimental`

Purpose:

- Enrich configurable feature settings with metadata CShells does not own.
- Keep small setting hints close to the setting property.
- `UiHint` remains a compatibility alias for packages that adopted the earlier
  casing; new code should use `UIHint`.

## ManifestUIOptionAttribute

Applies to public feature setting properties. May be used multiple times.

Supported metadata:

- Constructor `value`
- `Label`
- `Description`

Purpose:

- Supply static option values for setting UI hints such as `select-list`.
- Keep option values declarative and safe for metadata-only generation.

## ManifestUIOptionsProviderAttribute

Applies to public feature setting properties.

Supported metadata:

- Constructor `provider`
- `DependsOn`
- `Parameters`

Purpose:

- Reference a trusted Runtime Builder or elsaworkflows.io option provider for
  dynamic list values.
- Declare dependencies and simple parameters without executing package code.

## ManifestIgnoreAttribute

Applies to classes or properties.

Purpose:

- Exclude feature classes or feature setting properties from manifest generation.

## ManifestExtensionAttribute

Applies to classes or properties. Attribute-based extension metadata is limited
to simple string key/value pairs.

Supported metadata:

- `Key`
- `Value`

Purpose:

- Supply small extension metadata values.
- Rich extension payloads must be supplied through `elsa-package.overrides.json`.

## Rules

- CShells metadata is merged after inferred metadata and XML documentation.
- Manifest hint values are merged after CShells metadata.
- Override file values still win over all inferred, XML, CShells, and hint
  values.
- Hint values must be representable without executing package code.
