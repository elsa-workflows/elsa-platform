namespace ValenceControl.PackageCatalog.Abstractions.Compatibility;

public static class RuntimeKindCompatibilityPolicy
{
    public static bool IsCompatible(IReadOnlyList<string>? featureRuntimeKinds, IReadOnlyList<string>? runtimeKinds)
    {
        var normalizedFeatureRuntimeKinds = Normalize(featureRuntimeKinds);
        var normalizedRuntimeKinds = Normalize(runtimeKinds);
        // Runtime kinds are an opt-in constraint. A package or feature that declares none makes no
        // claim about where it runs, so it is offered for any image rather than hidden: treating
        // "undeclared" as "incompatible" excluded every feature whose manifest predates the
        // compatibility block, which is most of them.
        return normalizedRuntimeKinds.Count == 0
            || normalizedFeatureRuntimeKinds.Count == 0
            || normalizedFeatureRuntimeKinds.Intersect(normalizedRuntimeKinds, StringComparer.OrdinalIgnoreCase).Any();
    }

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? runtimeKinds) =>
        runtimeKinds?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}
