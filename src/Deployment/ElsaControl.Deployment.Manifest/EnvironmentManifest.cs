namespace ElsaControl.Deployment.Manifest;

public sealed record EnvironmentManifest
{
    public string? ApiVersion { get; init; }

    public string? Kind { get; init; }

    public ManifestMetadata Metadata { get; init; } = new();

    public ManifestResources Resources { get; init; } = new();
}
