namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

public static class EndpointPathPolicy
{
    public static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;

    public static bool IsSafe(string? path) =>
        IsSafePath(path);

    private static bool IsSafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.StartsWith("/", StringComparison.Ordinal)
            || path.Any(char.IsWhiteSpace)
            || path.Any(char.IsControl)
            || path.Contains('%', StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains('?', StringComparison.Ordinal)
            || path.Contains('#', StringComparison.Ordinal))
            return false;

        if (path == "/")
            return true;

        var segments = path.Split('/');
        if (segments[0].Length != 0 || segments[1..].Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
            return false;

        var canonicalPath = "/" + string.Join("/", segments[1..]);
        return string.Equals(path, canonicalPath, StringComparison.Ordinal);
    }
}
