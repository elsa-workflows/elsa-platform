using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.PackageManifests;

public abstract class ExtensibleManifestObject
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new();
}
