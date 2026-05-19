using Elsa.Platform.PackageManifest.Generator.Core.Generation;
using Elsa.Platform.PackageManifest.Generator.Core.Validation;

namespace Elsa.Platform.PackageManifest.Generator.Core.Overrides;

public sealed class ManifestOverrideReferenceResolver
{
    public void ValidateReferences(ManifestOverride? manifestOverride, IReadOnlyList<DiscoveredFeature> features, GenerationDiagnostics diagnostics)
    {
        if (manifestOverride is null)
            return;

        foreach (var featureOverride in manifestOverride.Features)
        {
            var feature = ResolveFeature(featureOverride, features);
            if (feature is null)
            {
                diagnostics.Warning("EPMGEN_OVERRIDE_FEATURE_UNKNOWN", "Override references a feature that was not discovered.", featureOverride.Id ?? featureOverride.ClrTypeName);
                continue;
            }

            foreach (var settingOverride in featureOverride.Settings ?? [])
            {
                if (feature.Settings.All(x => !string.Equals(x.Name, settingOverride.Name, StringComparison.OrdinalIgnoreCase)))
                    diagnostics.Warning("EPMGEN_OVERRIDE_SETTING_UNKNOWN", $"Override references unknown setting '{settingOverride.Name}'.", feature.FeatureId);
            }
        }
    }

    public static FeatureOverride? ResolveFeatureOverride(DiscoveredFeature feature, ManifestOverride? manifestOverride) =>
        manifestOverride?.Features.FirstOrDefault(x =>
            string.Equals(x.Id, feature.FeatureId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ClrTypeName, feature.ClrTypeName, StringComparison.OrdinalIgnoreCase));

    private static DiscoveredFeature? ResolveFeature(FeatureOverride featureOverride, IReadOnlyList<DiscoveredFeature> features) =>
        features.FirstOrDefault(x =>
            string.Equals(x.FeatureId, featureOverride.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ClrTypeName, featureOverride.ClrTypeName, StringComparison.OrdinalIgnoreCase));
}
