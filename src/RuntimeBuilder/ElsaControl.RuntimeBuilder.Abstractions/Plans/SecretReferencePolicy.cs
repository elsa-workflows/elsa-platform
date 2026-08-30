namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>Validates provider-backed secret locators retained in a resolved plan.</summary>
public static class SecretReferencePolicy
{
    public const string InvalidReferenceMessage = "Secret references must be canonical non-root secret:// locators without credentials, query strings, fragments, or ambiguous path forms.";

    public static bool IsSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(char.IsWhiteSpace)
            || value.Any(char.IsControl)
            || value.Contains('%')
            || value.Contains('\\')
            || value.Contains("/../", StringComparison.Ordinal)
            || value.EndsWith("/..", StringComparison.Ordinal)
            || value.Contains("/./", StringComparison.Ordinal)
            || value.EndsWith("/.", StringComparison.Ordinal)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var canonicalPath = "/" + string.Join("/", pathSegments);
        return string.Equals(uri.Scheme, "secret", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && !string.IsNullOrEmpty(uri.AbsolutePath)
            && uri.AbsolutePath != "/"
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && !uri.AbsolutePath.Contains("//", StringComparison.Ordinal)
            && !pathSegments.Any(segment => segment is "." or "..")
            && string.Equals(uri.AbsolutePath, canonicalPath, StringComparison.Ordinal);
    }
}
