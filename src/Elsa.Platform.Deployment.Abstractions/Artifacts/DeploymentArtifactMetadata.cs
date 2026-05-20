namespace Elsa.Platform.Deployment.Abstractions.Artifacts;

/// <summary>
/// Describes artifact build and source metadata.
/// </summary>
public sealed record DeploymentArtifactMetadata
{
    public DeploymentArtifactMetadata(
        DeploymentArtifactIdentity identity,
        DateTimeOffset builtAt,
        string? builder = null,
        string? source = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        Identity = identity;
        BuiltAt = builtAt.ToUniversalTime();
        Builder = string.IsNullOrWhiteSpace(builder) ? null : builder.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        Properties = properties ?? EmptyProperties;
    }

    public DeploymentArtifactIdentity Identity { get; }

    public DateTimeOffset BuiltAt { get; }

    public string? Builder { get; }

    public string? Source { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties = new Dictionary<string, string>();
}
