using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace Elsa.Platform.Deployment.Manifest;

internal static class ManifestEmpty
{
    public static IReadOnlyDictionary<string, string> StringDictionary { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public static IReadOnlyDictionary<string, JsonNode?> JsonNodeDictionary { get; } =
        new ReadOnlyDictionary<string, JsonNode?>(new Dictionary<string, JsonNode?>());
}
