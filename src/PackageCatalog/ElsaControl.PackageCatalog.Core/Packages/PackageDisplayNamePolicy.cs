namespace ElsaControl.PackageCatalog.Core.Packages;

public static class PackageDisplayNamePolicy
{
    // Keep in sync with the build-time generator's NamingHelpers.ToPackageDisplayName rule.
    private const string ElsaPackagePrefix = "Elsa.";

    public static string DefaultForPackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return packageId;

        var trimmed = packageId.Trim();
        return trimmed.StartsWith(ElsaPackagePrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[ElsaPackagePrefix.Length..]
            : trimmed;
    }
}
