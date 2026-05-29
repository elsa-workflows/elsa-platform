namespace Elsa.Platform.Workflows.RuntimeApplier;

internal static class WorkflowArtifactRuntimeVersionRange
{
    public static bool Includes(string? range, string? version)
    {
        if (string.IsNullOrWhiteSpace(range))
            return true;
        if (string.IsNullOrWhiteSpace(version) || !Version.TryParse(Normalize(version), out var candidate))
            return false;

        return range
            .Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(expression => IncludesExpression(expression, candidate));
    }

    private static bool IncludesExpression(string expression, Version candidate)
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

    private static bool IncludesSingle(string value, Version candidate)
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

        return TryCompare(value, exact => candidate == exact);
    }

    private static bool IncludesBracketRange(string value, Version candidate)
    {
        var inclusiveMin = value[0] == '[';
        var inclusiveMax = value[^1] == ']';
        var parts = value[1..^1].Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!string.IsNullOrWhiteSpace(parts[0]))
        {
            if (!Version.TryParse(Normalize(parts[0]), out var min))
                return false;

            var minOk = inclusiveMin ? candidate >= min : candidate > min;
            if (!minOk)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            if (!Version.TryParse(Normalize(parts[1]), out var max))
                return false;

            var maxOk = inclusiveMax ? candidate <= max : candidate < max;
            if (!maxOk)
                return false;
        }

        return true;
    }

    private static bool TryCompare(string value, Func<Version, bool> compare) =>
        Version.TryParse(Normalize(value), out var boundary) && compare(boundary);

    private static bool IsBracketRange(string value) =>
        (value.StartsWith('[') || value.StartsWith('(')) && (value.EndsWith(']') || value.EndsWith(')'));

    private static string Normalize(string value)
    {
        var core = value.Trim().Split('-', 2)[0].Split('+', 2)[0];
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => core
        };
    }
}
