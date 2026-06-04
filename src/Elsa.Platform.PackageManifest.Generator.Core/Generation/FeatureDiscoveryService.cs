using System.Reflection;
using Elsa.Platform.PackageManifest.Generator.Core.AssemblyInspection;

namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

public sealed class FeatureDiscoveryService(
    FeatureTypeMatcher featureTypeMatcher,
    FeatureMetadataReader metadataReader,
    SettingDiscoveryService settingDiscoveryService)
{
    public IReadOnlyList<DiscoveredFeature> Discover(Assembly assembly, ProjectPackageMetadata packageMetadata)
    {
        return assembly.GetTypes()
            .Where(featureTypeMatcher.IsFeature)
            .Select(type => CreateFeature(type, packageMetadata))
            .OrderBy(x => x.FeatureId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            null,
            type.GetInterfaces().Any(x => x.FullName == FeatureTypeMatcher.ShellFeatureInterfaceName)
                ? FeatureDiscoverySource.IShellFeature
                : FeatureDiscoverySource.InheritedIShellFeature,
            type.IsPublic,
            type.IsAbstract,
            type.IsGenericTypeDefinition,
            false,
            false,
            metadata.Dependencies.Select(x => new ManifestDependencyReference(null, null, $"{packageMetadata.PackageId}.{x}")).ToArray(),
            [],
            [],
            metadata.Infrastructure,
            null,
            metadata.Extensions,
            settings);
    }
}
