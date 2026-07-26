using System.Text.Json;

namespace ValenceControl.PackageManifests;

/// <summary>
/// A consumer-agnostic setting option resolved from a manifest's UI/validation metadata. Consumers project
/// this into their own option shape (e.g. converting <see cref="Value"/> to <c>JsonNode</c> where required).
/// </summary>
public readonly record struct ManifestSettingOption(string Label, JsonElement? Value, string? Description);

/// <summary>
/// Bridges the <see cref="FeatureManifest"/>/<see cref="FeatureSettingManifest"/> wire model into the
/// normalized shapes consumer apps need, without projecting into any consumer's own record types.
/// </summary>
public static class FeatureManifestExtensions
{
    /// <summary>
    /// The generator qualifies a feature's own <c>[ShellFeature(DependsOn = ...)]</c> references with its declaring
    /// package ID (e.g. <c>"Elsa.JavaScript.JintEngine"</c>), but the runtime feature catalog keys features by their
    /// short CShells ID (e.g. <c>"JintEngine"</c>). Strip that same-package prefix so manifest-declared dependencies
    /// line up with the IDs the cascade (and the runtime-resolved path) actually uses.
    /// </summary>
    public static IReadOnlyList<string> GetNormalizedDependencyIds(this FeatureManifest feature, string? packageId)
    {
        var prefix = string.IsNullOrWhiteSpace(packageId) ? null : packageId + ".";

        return feature.Dependencies
            .Select(dependency => dependency.FeatureId)
            .Where(featureId => !string.IsNullOrWhiteSpace(featureId))
            .Select(featureId => prefix is not null && featureId!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? featureId[prefix.Length..]
                : featureId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The generator nests static options under <c>ui.options.items</c> and provider-backed options under
    /// <c>ui.options.provider</c> (with <c>ui.options.source</c> discriminating the two); this also accepts a flat
    /// <c>ui.options</c> array for forward/backward compatibility, then falls back to <c>validation.enum</c>.
    /// </summary>
    public static (IReadOnlyList<ManifestSettingOption> Options, string? OptionsProvider) GetSettingOptions(this FeatureSettingManifest setting)
    {
        var ui = setting.UI;
        var validation = setting.Validation;

        if (ui.GetElement("options") is { } optionsElement)
        {
            if (optionsElement.ValueKind is JsonValueKind.Object)
            {
                if (string.Equals(optionsElement.GetJsonString("source"), "provider", StringComparison.OrdinalIgnoreCase))
                    return ([], optionsElement.GetJsonString("provider"));

                if (optionsElement.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Array)
                    return (MapOptions(items), null);
            }
            else if (optionsElement.ValueKind is JsonValueKind.Array)
            {
                return (MapOptions(optionsElement), null);
            }
        }

        if (validation.GetElement("enum") is { ValueKind: JsonValueKind.Array } enumValues)
            return (MapOptions(enumValues), null);

        return ([], null);
    }

    private static IReadOnlyList<ManifestSettingOption> MapOptions(JsonElement options) =>
        options.EnumerateArray()
            .Select(option => option.ValueKind is JsonValueKind.Object
                ? new ManifestSettingOption(
                    option.GetJsonString("label") ?? (option.TryGetProperty("value", out var v) ? JsonValueToDisplayText(v) : ""),
                    option.TryGetProperty("value", out var value) ? value.Clone() : null,
                    option.GetJsonString("description"))
                : new ManifestSettingOption(JsonValueToDisplayText(option), option.Clone(), null))
            .ToArray();

    private static string JsonValueToDisplayText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => value.GetRawText()
    };
}
