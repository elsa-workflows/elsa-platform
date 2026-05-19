namespace Elsa.PackageManifests.Licensing;

public sealed class LicenseManifest : ExtensibleManifestObject
{
    public string? Expression { get; init; }
    public string? Url { get; init; }
    public bool RequiresAcceptance { get; init; }
    public Dictionary<string, object?> Extensions { get; init; } = new();
}
