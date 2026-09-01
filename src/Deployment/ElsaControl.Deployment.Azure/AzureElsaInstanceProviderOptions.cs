namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Explicit authority used by the managed-instance Azure adapter. The adapter
/// remains fail-closed when a host has not configured a real provider scope.
/// </summary>
public sealed record AzureElsaInstanceProviderOptions
{
    public const string ConfigurationSection = "Deployment:AzureProvider:InstanceLifecycle";

    public bool Enabled { get; init; }

    /// <summary>Fingerprint of the checked-in template authority.</summary>
    public string TemplateFingerprint { get; init; } = "";

    /// <summary>Provider scope fingerprint bound by the runner.</summary>
    public string? ProviderScopeFingerprint { get; init; }

    public void Validate()
    {
        if (!Enabled)
            throw new InvalidOperationException("The managed-instance Azure provider is not enabled.");
        if (!IsFingerprint(TemplateFingerprint))
            throw new ArgumentException("A valid Azure template fingerprint is required.", nameof(TemplateFingerprint));
        if (!IsFingerprint(ProviderScopeFingerprint))
            throw new ArgumentException("The Azure provider scope fingerprint is invalid.", nameof(ProviderScopeFingerprint));
    }

    private static bool IsFingerprint(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
