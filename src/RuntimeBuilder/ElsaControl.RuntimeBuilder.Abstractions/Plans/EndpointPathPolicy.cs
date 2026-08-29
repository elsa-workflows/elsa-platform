namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

public static class EndpointPathPolicy
{
    public static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;

    public static bool IsSafe(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith("/", StringComparison.Ordinal)
        && !path.Any(char.IsWhiteSpace)
        && !path.Any(char.IsControl)
        && !path.Contains('%', StringComparison.Ordinal)
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Contains("//", StringComparison.Ordinal)
        && !path.Contains('?', StringComparison.Ordinal)
        && !path.Contains('#', StringComparison.Ordinal)
        && !path.Split('/').Any(segment => segment is "." or "..");
}
