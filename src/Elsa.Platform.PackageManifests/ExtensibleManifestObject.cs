using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Platform.PackageManifests;

public abstract class ExtensibleManifestObject
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new();
}
