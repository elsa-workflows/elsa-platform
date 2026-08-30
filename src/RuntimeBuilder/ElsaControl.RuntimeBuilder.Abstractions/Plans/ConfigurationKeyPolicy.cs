namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>
/// Defines the canonical, provider-neutral form of a configuration key. The length
/// matches the catalog's persisted feature-setting limit.
/// </summary>
public static class ConfigurationKeyPolicy
{
    public const int MaxLength = 256;

    public static bool IsSafe(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Length > MaxLength
            || !string.Equals(key, key.Trim(), StringComparison.Ordinal))
            return false;

        return key.All(IsSafeCharacter);
    }

    private static bool IsSafeCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '.' or ':' or '_' or '-';
}
