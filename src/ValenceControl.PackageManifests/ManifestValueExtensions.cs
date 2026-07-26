using System.Text.Json;

namespace ValenceControl.PackageManifests;

/// <summary>
/// Low-level accessors over the manifest wire model's <c>Dictionary&lt;string, object?&gt;</c> bags
/// (<see cref="FeatureSettingManifest.UI"/>, <see cref="FeatureSettingManifest.Validation"/>,
/// <see cref="FeatureSettingManifest.Extensions"/>, etc.), whose values deserialize as boxed
/// <see cref="JsonElement"/> instances.
/// </summary>
public static class ManifestValueExtensions
{
    /// <summary>Returns the boxed <see cref="JsonElement"/> stored under <paramref name="key"/>, or <c>null</c> when absent or not a <see cref="JsonElement"/>.</summary>
    public static JsonElement? GetElement(this IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is JsonElement element ? element : null;

    /// <summary>Returns the string value stored under <paramref name="key"/>, or <c>null</c> when absent or not a JSON string.</summary>
    public static string? GetString(this IReadOnlyDictionary<string, object?> values, string key) =>
        GetElement(values, key) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;

    /// <summary>Returns <c>true</c> only when <paramref name="key"/> holds the JSON literal <c>true</c>.</summary>
    public static bool GetBool(this IReadOnlyDictionary<string, object?> values, string key) =>
        GetElement(values, key) is { ValueKind: JsonValueKind.True };

    /// <summary>Returns the string value of <paramref name="property"/> on an object element, or <c>null</c> when the element is not an object, the property is missing, or its value is not a JSON string.</summary>
    public static string? GetJsonString(this JsonElement element, string property) =>
        element.ValueKind is JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
