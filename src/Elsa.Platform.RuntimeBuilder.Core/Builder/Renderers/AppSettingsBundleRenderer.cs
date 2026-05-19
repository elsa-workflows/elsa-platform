using Elsa.Platform.RuntimeBuilder.Abstractions;
using System.Text.Json;

namespace Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers;

public sealed class AppSettingsBundleRenderer(BundleFindingPolicy findingPolicy) : IBundleFileRenderer
{
    public int Order => 10;

    public BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings)
    {
        var featureSettings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in context.Packages)
        {
            foreach (var feature in package.Features.Where(x => package.SelectedFeatures.Contains(x.Id, StringComparer.OrdinalIgnoreCase)))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                package.Settings.TryGetValue(feature.Id, out var submittedSettings);
                foreach (var setting in feature.Settings.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    JsonElement? value = submittedSettings is not null && submittedSettings.TryGetValue(setting.Name, out var submittedValue)
                        ? submittedValue
                        : null;
                    values[setting.Name] = findingPolicy.MaterializeSettingValue(
                        value is { ValueKind: JsonValueKind.Undefined } ? null : value,
                        setting,
                        findings,
                        $"feature:{feature.Id}/setting:{setting.Name}");
                }

                featureSettings[feature.Id] = values;
            }
        }

        var document = new
        {
            Runtime = new
            {
                Image = new
                {
                    context.RuntimeImage.Slug,
                    Repository = context.RuntimeImage.Image,
                    Tag = context.ImageTag,
                    Port = context.RuntimeImage.DefaultPort
                }
            },
            Elsa = new
            {
                Packages = context.Packages.Select(package => new
                {
                    package.SourceId,
                    package.PackageId,
                    package.Version,
                    Features = package.SelectedFeatures
                }),
                FeatureSettings = featureSettings,
                Infrastructure = context.Infrastructure.Select(provider => new
                {
                    provider.Kind,
                    ProviderId = provider.Id,
                    provider.Strategy,
                    provider.Provider
                })
            }
        };

        var contents = JsonSerializer.Serialize(document, JsonOptions()) + Environment.NewLine;
        return new BundleFile("config.json", "json", "application/json", true, contents);
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
