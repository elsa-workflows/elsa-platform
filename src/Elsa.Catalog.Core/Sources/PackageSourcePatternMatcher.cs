using System.Text.RegularExpressions;

namespace Elsa.Catalog.Core.Sources;

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

        var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
