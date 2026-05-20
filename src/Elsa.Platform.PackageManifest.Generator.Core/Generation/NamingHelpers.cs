namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

internal static class NamingHelpers
{
    // Keep in sync with the runtime catalog's PackageDisplayNamePolicy.DefaultForPackageId rule.
    private const string ElsaPackagePrefix = "Elsa.";

    public static string ToDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsWhiteSpace(value[i - 1]))
                chars.Add(' ');
            chars.Add(value[i]);
        }

        return new string(chars.ToArray());
    }

    public static string ToPackageDisplayName(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return packageId;

        var trimmed = packageId.Trim();
        return trimmed.StartsWith(ElsaPackagePrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[ElsaPackagePrefix.Length..]
            : trimmed;
    }
}
