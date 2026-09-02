using NuGet.Versioning;

namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>
/// Enforces the accepted extension-code isolation policy at the resolved-plan boundary.
/// Arbitrary customer code remains disabled until the separately reviewed Private path exists.
/// </summary>
public static class ResolvedExtensionPolicy
{
    public static bool IsKnownIsolation(string? isolation) =>
        isolation is not null &&
        (isolation.Equals("Shared", StringComparison.OrdinalIgnoreCase) ||
         isolation.Equals("Data-isolated", StringComparison.OrdinalIgnoreCase) ||
         isolation.Equals("Dedicated", StringComparison.OrdinalIgnoreCase) ||
         isolation.Equals("Private", StringComparison.OrdinalIgnoreCase));

    public static bool IsAvailableForManagedLaunch(string? isolation) =>
        isolation?.Equals("Dedicated", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsAllowed(string? isolation, ResolvedExtensionClass extensionClass)
    {
        if (string.IsNullOrWhiteSpace(isolation) ||
            !Enum.IsDefined(extensionClass) ||
            extensionClass == ResolvedExtensionClass.Unspecified)
            return false;

        if (extensionClass == ResolvedExtensionClass.ArbitraryCustomer)
            return false;

        if (!IsKnownIsolation(isolation))
            return false;

        return extensionClass switch
        {
            ResolvedExtensionClass.BuiltIn => true,
            ResolvedExtensionClass.ValenceApproved =>
                isolation!.Equals("Dedicated", StringComparison.OrdinalIgnoreCase) ||
                isolation.Equals("Private", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

public static class ResolvedPackageVersionPolicy
{
    public static bool IsExact(string? version) =>
        !string.IsNullOrWhiteSpace(version) &&
        version.Length <= 256 &&
        !version.Any(char.IsControl) &&
        !version.Any(char.IsWhiteSpace) &&
        NuGetVersion.TryParse(version, out var parsed) &&
        string.Equals(parsed.ToNormalizedString(), version, StringComparison.Ordinal);
}
