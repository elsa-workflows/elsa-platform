using System.Text.Json;
using Elsa.Platform.PackageManifests;

namespace Elsa.Platform.Api.Public;

internal static class PublicJsonMetadata
{
    public static JsonElement? ParseValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyDictionary<string, JsonElement> ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, ManifestJsonSerializerOptions.Default) ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }
}
