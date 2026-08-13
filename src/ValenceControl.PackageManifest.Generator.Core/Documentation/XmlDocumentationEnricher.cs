using ValenceControl.PackageManifest.Generator.Core.Generation;

namespace ValenceControl.PackageManifest.Generator.Core.Documentation;

public sealed class XmlDocumentationEnricher
{
    public IReadOnlyList<DiscoveredFeature> Enrich(
        IReadOnlyList<DiscoveredFeature> features,
        IReadOnlyDictionary<string, XmlDocumentationEntry> entries)
    {
        return features.Select(feature =>
        {
            entries.TryGetValue($"T:{feature.ClrTypeName}", out var featureDoc);
            var settings = feature.Settings.Select(setting =>
            {
                entries.TryGetValue($"P:{feature.ClrTypeName}.{setting.ClrPropertyName}", out var settingDoc);
                return setting with
                {
                    Description = setting.Description ?? settingDoc?.Summary
                };
            }).ToArray();

            return feature with
            {
                Description = feature.Description ?? featureDoc?.Summary,
                Settings = settings
            };
        }).ToArray();
    }
}
