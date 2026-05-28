using System.Text.RegularExpressions;

namespace Elsa.Platform.PackageCatalog.Core.Sources;

public sealed class PackageSourcePatternMatcher
{
    public bool IsMatch(string packageId, IReadOnlyCollection<string> includePatterns, IReadOnlyCollection<string> excludePatterns)
    {
        if (excludePatterns.Any(pattern => Matches(packageId, pattern)))
            return false;

        return includePatterns.Any(pattern => Matches(packageId, pattern));
    }

    private static bool Matches(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var trimmed = pattern.Trim();
        if (IsDottedPrefixPattern(trimmed))
            return value.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(trimmed).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool IsDottedPrefixPattern(string pattern) =>
        pattern.EndsWith('.') && !pattern.Contains('*') && !pattern.Contains('?');
}
