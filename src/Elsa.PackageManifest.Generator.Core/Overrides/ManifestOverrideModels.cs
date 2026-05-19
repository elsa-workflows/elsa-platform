using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.PackageManifest.Generator.Core.Overrides;

public sealed class ManifestOverride
{
    public PackageOverride? Package { get; init; }
    public IReadOnlyList<FeatureOverride> Features { get; init; } = [];
}

public sealed class PackageOverride
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public DocumentationOverride? Documentation { get; init; }
    public IconOverride? Icon { get; init; }
    public LicenseOverride? License { get; init; }
    public CompatibilityOverride? Compatibility { get; init; }
    public IReadOnlyList<DependencyOverride>? Dependencies { get; init; }
    public IReadOnlyList<ConflictOverride>? Conflicts { get; init; }
    public IReadOnlyList<string>? RequiredCapabilities { get; init; }
    public Dictionary<string, object?>? Extensions { get; init; }
}

public sealed class FeatureOverride
{
    public string? Id { get; init; }
    public string? ClrTypeName { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public bool? Advanced { get; init; }
    public bool? Experimental { get; init; }
    public CompatibilityOverride? Compatibility { get; init; }
    public IReadOnlyList<DependencyOverride>? Dependencies { get; init; }
    public IReadOnlyList<ConflictOverride>? Conflicts { get; init; }
    public IReadOnlyList<string>? RequiredCapabilities { get; init; }
    public IReadOnlyList<InfrastructureRequirementOverride>? Infrastructure { get; init; }
    public IReadOnlyList<SettingOverride>? Settings { get; init; }
    public Dictionary<string, object?>? Extensions { get; init; }
}

public sealed class InfrastructureRequirementOverride
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool? Optional { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }
    public IReadOnlyList<string>? Providers { get; init; }
    public IReadOnlyList<string>? ConfigurationKeys { get; init; }
    public Dictionary<string, object?>? Extensions { get; init; }
}

public sealed class SettingOverride
{
    public string Name { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Group { get; init; }
    public bool? Required { get; init; }
    public bool? Nullable { get; init; }
    public object? DefaultValue { get; init; }
    public bool? Secret { get; init; }
    public bool? Sensitive { get; init; }
    public bool? RestartRequired { get; init; }
    public string? UIHint { get; init; }
    public JsonElement? UI { get; init; }
    public bool? Advanced { get; init; }
    public bool? Experimental { get; init; }
    public Dictionary<string, object?>? Extensions { get; init; }
}

public sealed class DocumentationOverride
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
    public string? Readme { get; init; }
    public string? Remarks { get; init; }
    public IReadOnlyList<string>? Examples { get; init; }
}

public sealed class IconOverride
{
    public string? Url { get; init; }
    public string? Path { get; init; }
}

public sealed class LicenseOverride
{
    public string? Expression { get; init; }
    public string? Url { get; init; }
}

public sealed class CompatibilityOverride
{
    public string? ElsaVersionRange { get; init; }
    public string? DockerImageVersionRange { get; init; }
    public IReadOnlyList<string>? RuntimeCapabilities { get; init; }
    public Dictionary<string, object?>? Extensions { get; init; }
}

public sealed class DependencyOverride
{
    public string PackageId { get; init; } = "";
    public string? VersionRange { get; init; }
    public string? FeatureId { get; init; }
}

public sealed class ConflictOverride
{
    public string PackageId { get; init; } = "";
    public string? VersionRange { get; init; }
    public string? FeatureId { get; init; }
    public string? Reason { get; init; }
}
