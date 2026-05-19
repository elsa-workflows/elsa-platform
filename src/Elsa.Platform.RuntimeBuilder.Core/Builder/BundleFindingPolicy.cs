using Elsa.Platform.RuntimeBuilder.Abstractions;
using System.Text.Json;

namespace Elsa.Platform.RuntimeBuilder.Core.Builder;

public sealed class BundleFindingPolicy
{
    public bool HasBlockingErrors(IEnumerable<BundleFinding> findings) =>
        findings.Any(x => string.Equals(x.Level, "error", StringComparison.OrdinalIgnoreCase));

    public string MaterializeSettingValue(JsonElement? value, ResolvedFeatureSetting setting, List<BundleFinding> findings, string scope)
    {
        if (setting.Secret)
        {
            if (value.HasValue && HasValue(value.Value))
                findings.Add(BundleFinding.Warning("setting.secretPlaceholder", $"{setting.DisplayName} is secret and is emitted as a placeholder.", scope));

            return CreatePlaceholder(setting);
        }

        if (value.HasValue && HasValue(value.Value))
            return ToStringValue(value.Value);

        if (!string.IsNullOrWhiteSpace(setting.DefaultValueJson))
            return ParseDefault(setting.DefaultValueJson!);

        if (setting.Required)
            findings.Add(BundleFinding.Warning("setting.placeholder", $"{setting.DisplayName} will be emitted as a placeholder.", scope));

        return CreatePlaceholder(setting);
    }

    public static string RedactPotentialSecret(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : "<redacted>";

    private static bool HasValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            _ => true
        };

    private static string ToStringValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();

    private static string ParseDefault(string defaultValueJson)
    {
        try
        {
            using var document = JsonDocument.Parse(defaultValueJson);
            return ToStringValue(document.RootElement);
        }
        catch (JsonException)
        {
            return defaultValueJson;
        }
    }

    private static string CreatePlaceholder(ResolvedFeatureSetting setting) =>
        $"${{{setting.EnvironmentVariable ?? setting.Name.ToUpperInvariant()}}}";
}
