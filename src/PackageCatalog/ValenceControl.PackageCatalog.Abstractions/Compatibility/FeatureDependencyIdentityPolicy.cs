namespace ValenceControl.PackageCatalog.Abstractions.Compatibility;

public static class FeatureDependencyIdentityPolicy
{
    public static bool Matches(string featureId, string? shellFeatureName, string? requestedFeatureId)
    {
        if (string.IsNullOrWhiteSpace(requestedFeatureId))
            return false;

        var requested = requestedFeatureId.Trim();
        if (string.Equals(featureId, requested, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(shellFeatureName))
            return false;

        var shellName = shellFeatureName.Trim();
        return string.Equals(shellName, requested, StringComparison.OrdinalIgnoreCase)
            || requested.EndsWith($".{shellName}", StringComparison.OrdinalIgnoreCase);
    }
}
