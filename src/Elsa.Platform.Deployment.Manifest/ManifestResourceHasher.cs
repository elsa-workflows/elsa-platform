using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Deployment.Manifest;

public static class ManifestResourceHasher
{
    public static ArtifactDigest Hash(string resourceType, object entry)
    {
        var node = JsonSerializer.SerializeToNode(entry) ?? new JsonObject();
        var canonical = Canonicalize(new JsonObject
        {
            ["resourceType"] = resourceType,
            ["desired"] = node
        });
        var json = canonical?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return new ArtifactDigest("sha256", Convert.ToHexString(bytes).ToLowerInvariant());
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => CanonicalizeObject(obj),
            JsonArray array => CanonicalizeArray(array),
            null => null,
            _ => JsonNode.Parse(node.ToJsonString())
        };
    }

    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var property in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
            result[property.Key] = Canonicalize(property.Value);
        return result;
    }

    private static JsonArray CanonicalizeArray(JsonArray array)
    {
        var result = new JsonArray();
        foreach (var item in array)
            result.Add(Canonicalize(item));
        return result;
    }
}
