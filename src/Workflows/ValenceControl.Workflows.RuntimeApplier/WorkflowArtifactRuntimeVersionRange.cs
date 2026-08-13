using NuGet.Versioning;

namespace ValenceControl.Workflows.RuntimeApplier;

internal static class WorkflowArtifactRuntimeVersionRange
{
    public static bool Includes(string? range, string? version)
    {
        if (string.IsNullOrWhiteSpace(range))
            return true;
        if (!TryParseVersion(version, out var candidate))
            return false;

        return range
            .Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(expression => IncludesExpression(expression, candidate));
    }

    private static bool IncludesExpression(string expression, NuGetVersion candidate)
    {
        var value = expression.Trim();
        if (value.Length == 0)
            return false;

        if (!IsBracketRange(value))
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
                return parts.All(part => IncludesExpression(part, candidate));
        }

        return IncludesSingle(value, candidate);
    }

    private static bool IncludesSingle(string value, NuGetVersion candidate)
    {
        if (value.StartsWith(">=", StringComparison.Ordinal))
            return TryCompare(value[2..], min => candidate >= min);
        if (value.StartsWith(">", StringComparison.Ordinal))
            return TryCompare(value[1..], min => candidate > min);
        if (value.StartsWith("<=", StringComparison.Ordinal))
            return TryCompare(value[2..], max => candidate <= max);
        if (value.StartsWith("<", StringComparison.Ordinal))
            return TryCompare(value[1..], max => candidate < max);

        if (IsBracketRange(value))
            return IncludesBracketRange(value, candidate);

        return TryCompare(value, exact => candidate.CompareTo(exact) == 0);
    }

    private static bool IncludesBracketRange(string value, NuGetVersion candidate)
    {
        var inclusiveMin = value[0] == '[';
        var inclusiveMax = value[^1] == ']';
        var parts = value[1..^1].Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!string.IsNullOrWhiteSpace(parts[0]))
        {
            if (!TryParseVersion(parts[0], out var min))
                return false;

            var minOk = inclusiveMin ? candidate >= min : candidate > min;
            if (!minOk)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            if (!TryParseVersion(parts[1], out var max))
                return false;

            var maxOk = inclusiveMax ? candidate <= max : candidate < max;
            if (!maxOk)
                return false;
        }

        return true;
    }

    private static bool TryCompare(string value, Func<NuGetVersion, bool> compare) =>
        TryParseVersion(value, out var boundary) && compare(boundary);

    private static bool IsBracketRange(string value) =>
        (value.StartsWith('[') || value.StartsWith('(')) && (value.EndsWith(']') || value.EndsWith(')'));

    private static bool TryParseVersion(string? value, out NuGetVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var suffixIndex = trimmed.IndexOfAny(['-', '+']);
        var core = suffixIndex < 0 ? trimmed : trimmed[..suffixIndex];
        var suffix = suffixIndex < 0 ? "" : trimmed[suffixIndex..];
        var parts = core.Split('.');
        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace))
            return false;

        var normalized = parts.Length switch
        {
            1 => $"{parts[0]}.0.0{suffix}",
            2 => $"{parts[0]}.{parts[1]}.0{suffix}",
            _ => trimmed
        };

        if (!NuGetVersion.TryParse(normalized, out var parsed))
            return false;

        version = parsed;
        return true;
    }
}
