namespace Elsa.Platform.Deployment.Abstractions.Artifacts;

/// <summary>
/// Identifies content by digest algorithm and value.
/// </summary>
public readonly record struct ArtifactDigest
{
    public ArtifactDigest(string algorithm, string value)
    {
        Algorithm = Require(algorithm, nameof(algorithm));
        Value = Require(value, nameof(value));
    }

    public string Algorithm { get; }

    public string Value { get; }

    public override string ToString() => $"{Algorithm}:{Value}";

    private static string Require(string value, string parameterName)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : normalized;
    }
}
