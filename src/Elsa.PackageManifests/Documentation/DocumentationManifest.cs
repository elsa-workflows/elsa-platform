namespace Elsa.PackageManifests.Documentation;

public sealed class DocumentationManifest : ExtensibleManifestObject
{
    public string? ReadmeUrl { get; init; }
    public string? ProjectUrl { get; init; }
    public string? ReleaseNotesUrl { get; init; }
    public string? ConfigurationUrl { get; init; }
    public Dictionary<string, object?> Extensions { get; init; } = new();
}
