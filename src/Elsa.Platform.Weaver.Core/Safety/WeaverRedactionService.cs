using System.Text.RegularExpressions;

namespace Elsa.Platform.Weaver.Core.Safety;

public sealed class WeaverRedactionService
{
    private static readonly Regex SecretAssignment = new(
        @"(?<key>(api[_-]?key|token|secret|password|connectionstring|authorization)\s*[:=]\s*)(?<value>[^,\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerToken = new(
        @"Bearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RedactionResult Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return new RedactionResult(value ?? string.Empty, false);

        var redacted = BearerToken.Replace(value, "Bearer [REDACTED]");
        redacted = SecretAssignment.Replace(redacted, "${key}[REDACTED]");
        return new RedactionResult(redacted, !string.Equals(value, redacted, StringComparison.Ordinal));
    }
}

public sealed record RedactionResult(string Value, bool Redacted);
