using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValenceControl.PackageManifests;

namespace ValenceControl.PackageManifest.Generator.Core.Generation;

public static class DeterministicJsonSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(ElsaPackageManifest manifest) => JsonSerializer.Serialize(manifest, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(ManifestJsonSerializerOptions.Default)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        return options;
    }
}
