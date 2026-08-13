namespace ValenceControl.PackageCatalog.Abstractions.Compatibility;

public static class RuntimeKindCompatibilityPolicy
{
    public static bool IsCompatible(IReadOnlyList<string>? featureRuntimeKinds, IReadOnlyList<string>? runtimeKinds)
    {
        var normalizedFeatureRuntimeKinds = Normalize(featureRuntimeKinds);
        var normalizedRuntimeKinds = Normalize(runtimeKinds);
        return normalizedRuntimeKinds.Count == 0
            || (normalizedFeatureRuntimeKinds.Count > 0
                && normalizedFeatureRuntimeKinds.Intersect(normalizedRuntimeKinds, StringComparer.OrdinalIgnoreCase).Any());
    }

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? runtimeKinds) =>
        runtimeKinds?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}
