# Valence Control Healing component-manifest generator

Add this package as a private build dependency to emit `valence-control-healing-component-manifest.json` beside a .NET application's build output.

```xml
<PackageReference Include="ValenceControl.Healing.ComponentManifest.Generator.MSBuild" Version="..." PrivateAssets="all" />
```

The build must supply a revision through `ValenceControlHealingSourceRevision` (or `SourceRevisionId`). Set `ValenceControlHealingRepositoryUrl`, `ValenceControlHealingBuildId`, and `ValenceControlHealingManifestCreatedAt` when the corresponding build metadata is available. Generation fails closed when a required revision or hashable artifact is unavailable, or when a resolved path escapes its permitted root.
