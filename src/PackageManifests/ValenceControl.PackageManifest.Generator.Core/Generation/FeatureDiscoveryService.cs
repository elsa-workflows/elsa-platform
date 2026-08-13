using System.Reflection;
using ValenceControl.PackageManifest.Generator.Core.AssemblyInspection;
using ValenceControl.PackageManifest.Generator.Core.Overrides;

namespace ValenceControl.PackageManifest.Generator.Core.Generation;

public sealed class FeatureDiscoveryService(
    FeatureTypeMatcher featureTypeMatcher,
    FeatureMetadataReader metadataReader,
    SettingDiscoveryService settingDiscoveryService)
{
    public DiscoveredManifestMetadata Discover(Assembly assembly, ProjectPackageMetadata packageMetadata)
    {
        var packageMetadataHints = metadataReader.ReadPackageMetadata(assembly);
        var features = assembly.GetTypes()
            .Where(featureTypeMatcher.IsFeature)
            .Select(type => CreateFeature(type, packageMetadata))
            .OrderBy(x => x.FeatureId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DiscoveredManifestMetadata(
            ToCompatibility(packageMetadataHints.RuntimeKinds),
            features);
    }

    private DiscoveredFeature CreateFeature(Type type, ProjectPackageMetadata packageMetadata)
    {
        var metadata = metadataReader.ReadFeatureMetadata(type);
        var featureId = $"{packageMetadata.PackageId}.{metadata.FeatureName}";
        var settings = settingDiscoveryService.Discover(type, featureId, metadata.FeatureName);

        return new DiscoveredFeature(
            featureId,
            metadata.FeatureName,
            type.FullName ?? type.Name,
            metadata.DisplayName ?? NamingHelpers.ToDisplayName(metadata.FeatureName),
            metadata.Description,
            metadata.Categories,
            type.GetInterfaces().Any(x => x.FullName == FeatureTypeMatcher.ShellFeatureInterfaceName)
                ? FeatureDiscoverySource.IShellFeature
                : FeatureDiscoverySource.InheritedIShellFeature,
            type.IsPublic,
            type.IsAbstract,
            type.IsGenericTypeDefinition,
            false,
            false,
            metadata.Dependencies.Select(x => new ManifestDependencyReference(null, null, x)).ToArray(),
            [],
            [],
            metadata.Infrastructure,
            ToCompatibility(metadata.RuntimeKinds),
            metadata.Extensions,
            settings);
    }

    private static CompatibilityOverride? ToCompatibility(IReadOnlyList<string> runtimeKinds) =>
        runtimeKinds.Count == 0 ? null : new CompatibilityOverride { RuntimeKinds = runtimeKinds };
}
