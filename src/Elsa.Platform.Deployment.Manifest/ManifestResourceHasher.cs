using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Deployment.Manifest;

public static class ManifestResourceHasher
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };
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
            _ => JsonNode.Parse(node.ToJsonString())
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
