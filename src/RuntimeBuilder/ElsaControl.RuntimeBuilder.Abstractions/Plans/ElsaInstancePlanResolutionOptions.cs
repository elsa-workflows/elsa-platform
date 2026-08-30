using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>A governed capacity outcome available to the provider-neutral resolver.</summary>
public sealed record ElsaCapacityProfile(
    int MinReplicas,
    int MaxReplicas,
    int CpuMillicores,
    int MemoryMiB,
    int? EphemeralStorageMiB = null);

/// <summary>
/// Resolver policy is intentionally data-driven so adding a release line or profile
/// does not require an Elsa-version branch. Unknown profiles fail closed.
/// </summary>
public sealed record ElsaInstancePlanResolutionOptions(
    string RolloutRing = "internal",
    IReadOnlyDictionary<string, ElsaCapacityProfile>? CapacityProfiles = null,
    string DefaultEgress = "restricted",
    IReadOnlyDictionary<string, IReadOnlyList<string>>? FeaturePresets = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? PackagePolicies = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ConfigurationShapeRevisions = null,
    IReadOnlyDictionary<string, ElsaFeatureOverrideKind>? FeatureOverrideDefinitions = null,
    IReadOnlySet<string>? SupportedChannels = null,
    IReadOnlySet<string>? SupportedLifecycles = null)
{
    public static ElsaInstancePlanResolutionOptions Default { get; } = new();

    public IReadOnlyDictionary<string, ElsaCapacityProfile> EffectiveCapacityProfiles =>
        CapacityProfiles ?? new Dictionary<string, ElsaCapacityProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["standard-small"] = new(1, 1, 500, 1024, 1024),
            ["standard"] = new(1, 3, 1000, 2048, 4096)
        };

    public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectiveFeaturePresets =>
        FeaturePresets ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter"] = []
        };

    public IReadOnlyDictionary<string, IReadOnlyList<string>> EffectivePackagePolicies =>
        PackagePolicies ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["approved"] = []
        };

    public IReadOnlySet<string> EffectiveSupportedChannels =>
        SupportedChannels ?? new HashSet<string>(["stable", "preview", "alpha", "beta", "rc", "nightly", "lts"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> EffectiveSupportedLifecycles =>
        SupportedLifecycles ?? new HashSet<string>(["active", "supported", "stable", "preview", "experimental", "ga"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ElsaFeatureOverrideKind> EffectiveFeatureOverrideDefinitions =>
        FeatureOverrideDefinitions ?? new Dictionary<string, ElsaFeatureOverrideKind>(StringComparer.OrdinalIgnoreCase);
}
