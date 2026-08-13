using ValenceControl.PackageManifest.Generator.Core.Generation;

namespace ValenceControl.PackageManifest.Generator.Core.Validation;

public sealed class RecommendedMetadataValidator
{
    public void Validate(IReadOnlyList<DiscoveredFeature> features, bool strict, GenerationDiagnostics diagnostics)
    {
        foreach (var feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature.Description))
                diagnostics.Warning("EPMGEN_FEATURE_DESCRIPTION_MISSING", $"Feature '{feature.FeatureId}' has no description.", feature.ClrTypeName);

            if (!strict)
                continue;

            foreach (var setting in feature.Settings.Where(x => string.IsNullOrWhiteSpace(x.Description)))
                diagnostics.Warning("EPMGEN_SETTING_DESCRIPTION_MISSING", $"Setting '{setting.Name}' on feature '{feature.FeatureId}' has no description.", setting.ConfigurationPath);
        }
    }
}
