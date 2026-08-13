using System.Text.RegularExpressions;

namespace ValenceControl.PackageManifests.Compatibility;

public static partial class ElsaRuntimeKinds
{
    public const string Server = "elsa.server";
    public const string Studio = "elsa.studio";
}

public static partial class RuntimeKindCompatibility
{
    public static IReadOnlyList<string> DefaultRuntimeKinds { get; } = [];

    public static bool IsValid(string? runtimeKind) =>
        !string.IsNullOrWhiteSpace(runtimeKind) &&
        string.Equals(runtimeKind, runtimeKind.Trim(), StringComparison.Ordinal) &&
        RuntimeKindPattern().IsMatch(runtimeKind);

    public static bool Contains(IReadOnlyList<string> runtimeKinds, string runtimeKind) =>
        runtimeKinds.Contains(runtimeKind, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EffectiveRuntimeKinds(IReadOnlyList<string>? runtimeKinds) =>
        runtimeKinds is { Count: > 0 } ? runtimeKinds : DefaultRuntimeKinds;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeKindPattern();
}
