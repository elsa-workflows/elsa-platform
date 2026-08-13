namespace ValenceControl.Deployment.Manifest;

public sealed record ManifestMetadata
{
    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Environment { get; init; }

    public IReadOnlyDictionary<string, string> Labels { get; init; } = ManifestEmpty.StringDictionary;

    public IReadOnlyDictionary<string, string> Annotations { get; init; } = ManifestEmpty.StringDictionary;
}
