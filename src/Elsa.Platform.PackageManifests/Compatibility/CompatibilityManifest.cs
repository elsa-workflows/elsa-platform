namespace Elsa.Platform.PackageManifests.Compatibility;

/// <summary>
/// Explicit compatibility metadata for runtimes, Docker images, packages, and future checks.
/// </summary>
public sealed class CompatibilityManifest : ExtensibleManifestObject
{
    public IReadOnlyList<string> RuntimeKinds { get; init; } = [];
    public string? ElsaVersionRange { get; init; }
    public string? DockerImageVersionRange { get; init; }
    public IReadOnlyList<PackageCompatibilityRuleManifest> PackageRules { get; init; } = [];
    public IReadOnlyList<string> RuntimeCapabilities { get; init; } = [];
    public Dictionary<string, object?> Extensions { get; init; } = new();
}

public sealed class PackageCompatibilityRuleManifest : ExtensibleManifestObject
{
    public string PackageId { get; init; } = "";
    public string? VersionRange { get; init; }
    public string? Reason { get; init; }
}

public sealed class DependencyManifest : ExtensibleManifestObject
{
    public string? PackageId { get; init; }
    public string? VersionRange { get; init; }
    public string? FeatureId { get; init; }
    public bool Optional { get; init; }
    public string? Reason { get; init; }
    public Dictionary<string, object?> Extensions { get; init; } = new();
}

public sealed class ConflictManifest : ExtensibleManifestObject
{
    public string? PackageId { get; init; }
    public string? VersionRange { get; init; }
    public string? FeatureId { get; init; }
    public string? Reason { get; init; }
    public Dictionary<string, object?> Extensions { get; init; } = new();
}
