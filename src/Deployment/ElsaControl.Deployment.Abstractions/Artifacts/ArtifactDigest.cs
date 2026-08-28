using System.Text.Json.Serialization;
using static ElsaControl.Deployment.Abstractions.DeploymentGuard;

namespace ElsaControl.Deployment.Abstractions.Artifacts;

/// <summary>
/// Identifies content by digest algorithm and value.
/// </summary>
public readonly record struct ArtifactDigest
{
    [JsonConstructor]
    public ArtifactDigest(string algorithm, string value)
    {
        Algorithm = Require(algorithm, nameof(algorithm));
        Value = Require(value, nameof(value));
    }

    public string Algorithm { get; }

    public string Value { get; }

    public override string ToString() => $"{Algorithm}:{Value}";
}
