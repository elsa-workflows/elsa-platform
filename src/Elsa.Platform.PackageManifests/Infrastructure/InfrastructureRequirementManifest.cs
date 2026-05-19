namespace Elsa.Platform.PackageManifests.Infrastructure;

/// <summary>
/// Describes an abstract infrastructure dependency needed by a package feature.
/// </summary>
public sealed class InfrastructureRequirementManifest : ExtensibleManifestObject
{
    /// <summary>Stable requirement identifier within the declaring feature.</summary>
    public string Id { get; init; } = "";
    /// <summary>Abstract infrastructure kind such as database, message-broker, cache, blob-storage, smtp, or secret-store.</summary>
    public string Kind { get; init; } = "";
    public bool Optional { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> Providers { get; init; } = [];
    public IReadOnlyList<string> ConfigurationKeys { get; init; } = [];
    public Dictionary<string, object?> Extensions { get; init; } = new();
}
