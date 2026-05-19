using System.Text.Json.Serialization;

namespace Elsa.PackageManifests;

public sealed class FeatureSettingManifest : ExtensibleManifestObject
{
    public string Name { get; init; } = "";
    public string? ClrType { get; init; }
    public string JsonType { get; init; } = "";
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public string? Category { get; init; }
    public Dictionary<string, object?> Validation { get; init; } = new();
    public bool Secret { get; init; }
    public bool RestartRequired { get; init; }
    public string? EnvironmentVariable { get; init; }
    [JsonPropertyName("ui")]
    public Dictionary<string, object?> UI { get; init; } = new();
    public Dictionary<string, object?> Extensions { get; init; } = new();
}
