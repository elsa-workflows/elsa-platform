using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Deployment.Manifest;

public static class ManifestResourceHasher
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    // The hash wire schema intentionally uses PascalCase property names.
    // Entry record properties must not be renamed without a hash migration strategy.
    private static readonly JsonSerializerOptions HashSerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    public static ArtifactDigest Hash(string resourceType, object entry)
    {
        var node = JsonSerializer.SerializeToNode(entry, HashSerializerOptions) ?? new JsonObject();
        var canonical = Canonicalize(new JsonObject
        {
            ["resourceType"] = resourceType,
            ["desired"] = node
        });
        var json = canonical?.ToJsonString(CompactJsonOptions) ?? "null";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return new ArtifactDigest("sha256", Convert.ToHexString(bytes).ToLowerInvariant());
    }

    private static JsonNode? Canonicalize(JsonNode? node, string? propertyName = null)
    {
        return node switch
        {
            JsonObject obj => CanonicalizeObject(obj),
            JsonArray array => CanonicalizeArray(array, propertyName),
            null => null,
            JsonValue value => CanonicalizeValue(value),
            _ => JsonNode.Parse(node.ToJsonString(CompactJsonOptions))
        };
    }

    private static JsonNode? CanonicalizeValue(JsonValue value)
    {
        if (!value.TryGetValue<JsonElement>(out var element))
            return JsonNode.Parse(value.ToJsonString(CompactJsonOptions));

        return element.ValueKind switch
        {
            JsonValueKind.Number when decimal.TryParse(
                element.GetRawText(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var number) => JsonValue.Create(number),
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            _ => JsonNode.Parse(element.GetRawText())
        };
    }

    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var property in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
            result[property.Key] = Canonicalize(property.Value, property.Key);
        return result;
    }

    private static JsonArray CanonicalizeArray(JsonArray array, string? propertyName)
    {
        var items = array.Select(item => Canonicalize(item)).ToArray();
        if (string.Equals(propertyName, "dependencies", StringComparison.OrdinalIgnoreCase))
            items = items
                .OrderBy(item => item?.ToJsonString(CompactJsonOptions) ?? "null", StringComparer.Ordinal)
                .ToArray();

        var result = new JsonArray();
        foreach (var item in items)
            result.Add(item);
        return result;
    }
}
