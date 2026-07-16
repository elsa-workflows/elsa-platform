# Elsa Platform Healing component-manifest generator

Add this package as a private build dependency to emit `elsa-healing-component-manifest.json` beside a .NET application's build output.

```xml
<PackageReference Include="Elsa.Platform.Healing.ComponentManifest.Generator.MSBuild" Version="..." PrivateAssets="all" />
```

The build must supply a revision through `ElsaHealingSourceRevision` (or `SourceRevisionId`). Set `ElsaHealingRepositoryUrl`, `ElsaHealingBuildId`, and `ElsaHealingManifestCreatedAt` when the corresponding build metadata is available. Generation fails closed when a required revision or hashable artifact is unavailable, or when a resolved path escapes its permitted root.
