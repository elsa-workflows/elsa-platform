namespace Elsa.Platform.PackageCatalog.Core.Compatibility;

public sealed class VersionRangeEvaluator
{
    public bool Includes(string? range, string? version)
    {
        if (string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(version))
            return true;

        if (!Version.TryParse(Normalize(version), out var candidate))
            return false;

        var value = range.Trim();
        if (value.StartsWith(">=", StringComparison.Ordinal))
            return Version.TryParse(Normalize(value[2..]), out var min) && candidate >= min;

        if (value.StartsWith("<=", StringComparison.Ordinal))
            return Version.TryParse(Normalize(value[2..]), out var max) && candidate <= max;

        if ((value.StartsWith('[') || value.StartsWith('(')) && (value.EndsWith(']') || value.EndsWith(')')))
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

        return Version.TryParse(Normalize(value), out var exact) && candidate == exact;
    }

    private static string Normalize(string value)
    {
        var core = value.Trim().Split('-', 2)[0];
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => core
        };
    }
}
