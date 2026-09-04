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

    public string SubscriptionId { get; init; } = "";

    public string ResourceGroupNamePrefix { get; init; } = "";

    public int ResourceGroupNamingVersion { get; init; } = 1;

    public void Validate()
    {
        if (!Enabled)
            throw new InvalidOperationException("The managed-instance Azure provider is not enabled.");
        if (!IsFingerprint(TemplateFingerprint))
            throw new ArgumentException("A valid Azure template fingerprint is required.", nameof(TemplateFingerprint));
        if (!IsFingerprint(ProviderScopeFingerprint))
            throw new ArgumentException("The Azure provider scope fingerprint is invalid.", nameof(ProviderScopeFingerprint));
        if (!Guid.TryParseExact(SubscriptionId, "D", out _) ||
            !string.Equals(SubscriptionId, SubscriptionId.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("The Azure subscription ID is invalid.", nameof(SubscriptionId));
        _ = AzureProviderResourceAssignmentNaming.ResourceGroupName(
            ResourceGroupNamePrefix, Guid.Parse("11111111-1111-1111-1111-111111111111"), ResourceGroupNamingVersion);
    }

    private static bool IsFingerprint(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
