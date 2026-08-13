using System.Text.Json;

namespace ValenceControl.PackageManifest.Generator.Core.Generation;

public sealed class ManifestSurfaceComparer
{
    public string Normalize(string manifestJson)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var features = document.RootElement.TryGetProperty("features", out var featureElement)
            ? featureElement.EnumerateArray()
                .Select(feature => new
                {
                    id = feature.GetProperty("id").GetString(),
                    settings = feature.TryGetProperty("settings", out var settings)
                        ? settings.EnumerateArray()
                            .Select(setting => new
                            {
                                name = setting.GetProperty("name").GetString(),
                                clrType = setting.TryGetProperty("clrType", out var clrType) ? clrType.GetString() : null,
                                jsonType = setting.TryGetProperty("jsonType", out var jsonType) ? jsonType.GetString() : null,
                                required = setting.TryGetProperty("required", out var required) && required.GetBoolean(),
                                validation = setting.TryGetProperty("validation", out var validation) ? validation.GetRawText() : null
                            })
                            .OrderBy(x => x.name)
                            .ToArray()
                        : []
                })
                .OrderBy(x => x.id)
                .ToArray()
            : [];

        return JsonSerializer.Serialize(features);
    }
}
